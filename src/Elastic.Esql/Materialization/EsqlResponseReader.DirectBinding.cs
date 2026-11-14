// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if !NETSTANDARD2_0
using System.Text;
#endif
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
#if !NETSTANDARD2_0
	private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

#endif
	/// <summary>
	/// A validated cell's extent in the source, whether its bare scalar is read as <c>[value]</c>, whether a string cell
	/// carries an escape, and the value a built-in cell other than a string parsed to or the bits of an enum cell.
	/// </summary>
	private readonly record struct CellSpan(int Column, long Start, long Length, bool Wrap, bool IsEscaped, DirectValue Value);

	/// <summary>The parsed value of a built-in cell, in the field its <see cref="DirectBinderKind"/> selects.</summary>
	/// <remarks>
	/// The fields overlap, so the struct stays 16 bytes and unmanaged and a stackalloc'd row of <see cref="CellSpan"/>
	/// can carry each built-in cell other than a string, and the bits of each enum cell in <see cref="Int64"/>, from the
	/// validating pass to the assigning one without a second parse. A string is a reference, which cannot overlap the
	/// value fields and would make the row managed, so it travels in the per-enumeration scratch instead. Only
	/// sequential-layout fields overlap, because an auto-layout field such as DateTime inside an explicit layout is not
	/// guaranteed to load on every runtime the netstandard2.0 build serves. A date lives in <see cref="Int64"/>, so
	/// offset 8 is free for the offset of a DateTimeOffset whenever a date is the live kind.
	/// </remarks>
	[StructLayout(LayoutKind.Explicit)]
	private struct DirectValue
	{
		[FieldOffset(0)]
		public bool Bool;

		[FieldOffset(0)]
		public int Int32;

		[FieldOffset(0)]
		public long Int64;

		[FieldOffset(0)]
		public double Double;

		[FieldOffset(0)]
		public float Single;

		[FieldOffset(0)]
		public decimal Decimal;

		[FieldOffset(0)]
		public Guid Guid;

		[FieldOffset(8)]
		public short OffsetMinutes;
	}

	/// <summary>
	/// Reads one row's cells directly off the reader and assigns them through the binder's typed setters, or through
	/// the contract's <see cref="JsonPropertyInfo.Set"/> delegate where none was compiled. The reader must have just
	/// consumed the row's StartArray token, and <paramref name="source"/> is the sequence it reads. Returns false with
	/// <paramref name="incomplete"/> set when the buffer ends mid-row (caller re-reads with more data), or false with
	/// it unset when a cell's token shape requires the serializer's coercion or error semantics (caller falls back
	/// for this row).
	/// </summary>
	internal static bool TryBindRowDirect<T>(
		ref Utf8JsonReader reader,
		in ReadOnlySequence<byte> source,
		DirectRowBinder binder,
		RowAssemblyBuffers buffers,
		out T? item,
		out bool incomplete)
	{
		item = default;
		incomplete = false;

		if (binder.ValidatedCellCount > 0)
			return TryBindValidatedRow(ref reader, source, binder, buffers, out item, out incomplete);

		var kinds = binder.Kinds;
		var properties = binder.Properties;
		var typedSetters = binder.TypedSetters;

		var instance = binder.CreateObject();

		for (var i = 0; i < kinds.Length; i++)
		{
			if (!reader.Read())
			{
				incomplete = true;
				return false;
			}

			var tokenType = reader.TokenType;

			// Fewer cells than columns - the slow path raises the canonical JsonException.
			if (tokenType == JsonTokenType.EndArray)
				return false;

			if (tokenType == JsonTokenType.Null)
			{
				// A null cell leaves the property at its initializer, as the assembled row omits it. For a
				// required member that omission is the serializer's error to raise.
				if (properties[i].IsRequired)
					return false;
				continue;
			}

			if (!TryBindDirectValue(ref reader, kinds[i], tokenType, properties[i], typedSetters[i], instance))
				return false;
		}

		if (!reader.Read())
		{
			incomplete = true;
			return false;
		}

		// More cells than columns - the slow path raises the canonical JsonException.
		if (reader.TokenType != JsonTokenType.EndArray)
			return false;

		item = (T)instance;
		return true;
	}

	/// <summary>
	/// Binds a row with converter, string list or enum cells in three steps. The validating pass checks every cell's shape,
	/// parses each built-in cell other than a string and each enum cell, and assigns nothing, so a row that fails it or ends
	/// mid-buffer has run no nested type's deserialization callback. Once that pass has seen the closing bracket, each string
	/// cell and each string list cell decodes into the scratch, and a string that does not decode sends the row to the
	/// fallback before any converter cell runs. The assigning pass then assigns the cells in column order, the order the
	/// serializer's setters see. A cell that only the assigning pass decodes can still send the row to the fallback (a
	/// converter cell its contract rejects, a null converter result for a property that does not accept null) after the
	/// converter cells in that cell or before it have run their callbacks.
	/// </summary>
	private static bool TryBindValidatedRow<T>(
		ref Utf8JsonReader reader,
		in ReadOnlySequence<byte> source,
		DirectRowBinder binder,
		RowAssemblyBuffers buffers,
		out T? item,
		out bool incomplete)
	{
		item = default;
		var columnCount = binder.Kinds.Length;
		var strings = buffers.StringCells(columnCount);
		var lists = buffers.ListCells(columnCount);

		CellSpan[]? rentedSpans = null;
		var spans = columnCount <= 64
			? stackalloc CellSpan[columnCount]
			: (rentedSpans = ArrayPool<CellSpan>.Shared.Rent(columnCount)).AsSpan(0, columnCount);

		try
		{
			if (!TryValidateRow(ref reader, binder, spans, out var recorded, out incomplete))
				return false;

			// A row that ends mid-buffer is read again from its start after each fill of the stream buffer, so its strings
			// decode once the validating pass has seen the whole row. An undecodable string must reach the fallback before a
			// converter cell runs a callback.
			if (!TryDecodeStringCells(source, binder, buffers, spans[..recorded]))
				return false;

			var instance = binder.CreateObject();
			if (!TryAssignCells(source, binder, buffers, spans[..recorded], instance))
				return false;

			item = (T)instance;
			return true;
		}
		finally
		{
			// A string or a list left in the scratch stays reachable until a later row overwrites it or the enumeration ends.
			Array.Clear(strings, 0, columnCount);
			Array.Clear(lists, 0, columnCount);
			buffers.ClearStringListScratch();
			if (rentedSpans is not null)
				ArrayPool<CellSpan>.Shared.Return(rentedSpans);
		}
	}

	/// <summary>
	/// Checks every cell's token shape, skips over every converter cell, and records the extent of each non-null cell
	/// with the parsed value of each built-in cell other than a string and the bits of each enum cell. Returns false with
	/// <paramref name="incomplete"/> set when the buffer ends mid-row.
	/// </summary>
	private static bool TryValidateRow(
		ref Utf8JsonReader reader,
		DirectRowBinder binder,
		scoped Span<CellSpan> spans,
		out int recorded,
		out bool incomplete)
	{
		recorded = 0;
		incomplete = false;
		var kinds = binder.Kinds;

		for (var i = 0; i < kinds.Length; i++)
		{
			if (!reader.Read())
			{
				incomplete = true;
				return false;
			}

			var tokenType = reader.TokenType;

			// Fewer cells than columns - the slow path raises the canonical JsonException.
			if (tokenType == JsonTokenType.EndArray)
				return false;

			if (tokenType == JsonTokenType.Null)
			{
				// A null cell leaves the property at its initializer, as the assembled row omits it. For a
				// required member that omission is the serializer's error to raise.
				if (binder.Properties[i].IsRequired)
					return false;
				continue;
			}

			var start = reader.TokenStartIndex;
			var wrap = binder.IsCollection[i] && tokenType != JsonTokenType.StartArray;
			var isEscaped = kinds[i] == DirectBinderKind.String && reader.ValueIsEscaped;
			var value = default(DirectValue);

			if (kinds[i] == DirectBinderKind.Converter)
			{
				if (!reader.TrySkip())
				{
					incomplete = true;
					return false;
				}
			}
			else if (kinds[i] is DirectBinderKind.StringList or DirectBinderKind.StringArray)
			{
				if (!TryValidateStringList(ref reader, tokenType, binder.IsCollection[i], out incomplete))
					return false;
			}
			else if (kinds[i] == DirectBinderKind.Enum)
			{
				if (!TryReadEnumBits(ref reader, tokenType, binder.EnumTypeCodes[i], out value.Int64))
					return false;
			}
			else if (!TryReadDirectValue(ref reader, kinds[i], tokenType, out value))
				return false;

			spans[recorded++] = new CellSpan(i, start, reader.BytesConsumed - start, wrap, isEscaped, value);
		}

		if (!reader.Read())
		{
			incomplete = true;
			return false;
		}

		// More cells than columns - the slow path raises the canonical JsonException.
		return reader.TokenType == JsonTokenType.EndArray;
	}

	/// <summary>
	/// Checks that a string list cell is an array of strings and nulls, or a bare string the list takes as its only
	/// element. Returns false with <paramref name="incomplete"/> set when the buffer ends inside the array.
	/// </summary>
	private static bool TryValidateStringList(ref Utf8JsonReader reader, JsonTokenType tokenType, bool bareStringIsList, out bool incomplete)
	{
		incomplete = false;

		// A bare string reads as a list only where the layout wraps it for the assembly path, because elsewhere the fallback
		// hands it to the serializer unwrapped and the serializer rejects it.
		if (tokenType == JsonTokenType.String)
			return bareStringIsList;

		if (tokenType != JsonTokenType.StartArray)
			return false;

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndArray)
				return true;

			// The serializer's list converter rejects any other element, and the fallback raises its error.
			if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
				return false;
		}

		incomplete = true;
		return false;
	}

	private static bool TryDecodeStringCells(
		in ReadOnlySequence<byte> source,
		DirectRowBinder binder,
		RowAssemblyBuffers buffers,
		ReadOnlySpan<CellSpan> spans)
	{
		var strings = buffers.StringCells(binder.Kinds.Length);
		var lists = buffers.ListCells(binder.Kinds.Length);

		foreach (var span in spans)
		{
			var column = span.Column;
			var kind = binder.Kinds[column];
			if (kind is not (DirectBinderKind.String or DirectBinderKind.StringList or DirectBinderKind.StringArray))
				continue;

#if !NETSTANDARD2_0
			if (kind == DirectBinderKind.String && !span.IsEscaped && source.IsSingleSegment)
			{
				if (!TryDecodePlainString(source.First.Span.Slice((int)span.Start + 1, (int)span.Length - 2), out strings[column]))
					return false;

				continue;
			}
#endif

			var cellReader = CreateCellReader(source, span, default);
			_ = cellReader.Read();

			if (kind == DirectBinderKind.String)
			{
				if (!TryGetString(ref cellReader, cellReader.TokenType, out strings[column]))
					return false;
			}
			else if (kind == DirectBinderKind.StringArray)
			{
				var scratch = buffers.StringListScratch;
				// The scratch keeps the previous cell's elements until the row's finally clears it, so a row with two string[]
				// cells needs this clear before each decode.
				scratch.Clear();
				if (!TryDecodeStringList(ref cellReader, scratch))
					return false;
				lists[column] = scratch.ToArray();
			}
			else
			{
				var list = (List<string?>)binder.CellTypeInfos[column]!.CreateObject!();
				if (!TryDecodeStringList(ref cellReader, list))
					return false;
				lists[column] = list;
			}
		}

		return true;
	}

	/// <summary>
	/// Adds a validated string list cell's elements to <paramref name="target"/>, or returns false for an element the reader
	/// cannot decode.
	/// </summary>
	private static bool TryDecodeStringList(ref Utf8JsonReader cellReader, List<string?> target)
	{
		string? element;

		if (cellReader.TokenType != JsonTokenType.StartArray)
		{
			if (!TryGetString(ref cellReader, cellReader.TokenType, out element))
				return false;

			target.Add(element);
			return true;
		}

		while (cellReader.Read() && cellReader.TokenType != JsonTokenType.EndArray)
		{
			// The serializer adds a null element as null, because nullable annotations do not reach collection elements.
			if (cellReader.TokenType == JsonTokenType.Null)
			{
				target.Add(null);
				continue;
			}

			if (!TryGetString(ref cellReader, cellReader.TokenType, out element))
				return false;

			target.Add(element);
		}

		return true;
	}

	private static bool TryAssignCells(
		in ReadOnlySequence<byte> source,
		DirectRowBinder binder,
		RowAssemblyBuffers buffers,
		ReadOnlySpan<CellSpan> spans,
		object instance)
	{
		var strings = buffers.StringCells(binder.Kinds.Length);
		var lists = buffers.ListCells(binder.Kinds.Length);

		foreach (var span in spans)
		{
			var column = span.Column;
			var kind = binder.Kinds[column];

			if (kind == DirectBinderKind.Converter)
			{
				if (!TryBindConverterCell(source, span, binder, buffers, instance))
					return false;
				continue;
			}

			if (kind == DirectBinderKind.StringList)
			{
				Assign(binder.TypedSetters[column], binder.Properties[column], instance, (List<string?>)lists[column]!);
				continue;
			}

			if (kind == DirectBinderKind.StringArray)
			{
				Assign(binder.TypedSetters[column], binder.Properties[column], instance, (string[])lists[column]!);
				continue;
			}

			if (kind == DirectBinderKind.Enum)
			{
				AssignEnum(binder, column, span.Value.Int64, instance);
				continue;
			}

			AssignDirectValue(kind, span.Value, strings[column], binder.Properties[column], binder.TypedSetters[column], instance);
		}

		return true;
	}

	/// <summary>
	/// Deserializes one validated cell through its own contract and assigns it. A cell the contract rejects returns
	/// false so the slow path re-reads the row and raises the canonical error.
	/// </summary>
	private static bool TryBindConverterCell(
		in ReadOnlySequence<byte> source,
		in CellSpan span,
		DirectRowBinder binder,
		RowAssemblyBuffers buffers,
		object instance)
	{
		var state = CellReaderState(binder);

		// ES|QL returns a single-valued multi-value field as a bare scalar, and the row path rewrites it as [value].
		var cellReader = span.Wrap
			? new Utf8JsonReader(WrapInArray(source, span, buffers.RowBuffer), isFinalBlock: true, state)
			: CreateCellReader(source, span, state);

		_ = cellReader.Read();

		return binder.ConverterCells[span.Column]!.TryBind(ref cellReader, instance);
	}

	private static Utf8JsonReader CreateCellReader(in ReadOnlySequence<byte> source, in CellSpan span, JsonReaderState state) =>
		source.IsSingleSegment
			? new Utf8JsonReader(source.First.Span.Slice((int)span.Start, (int)span.Length), isFinalBlock: true, state)
			: new Utf8JsonReader(source.Slice(span.Start, span.Length), isFinalBlock: true, state);

	private static ReadOnlySpan<byte> WrapInArray(in ReadOnlySequence<byte> source, in CellSpan span, PooledBufferWriter buffer)
	{
		buffer.ResetWrittenCount();
		buffer.Write((byte)'[');

		if (source.IsSingleSegment)
			buffer.Write(source.First.Span.Slice((int)span.Start, (int)span.Length));
		else
			buffer.Write(source.Slice(span.Start, span.Length));

		buffer.Write((byte)']');
		return buffer.WrittenSpan;
	}

	// The reader's depth limit takes precedence over the options' one inside Deserialize, and the assembled row
	// object sits one level above every cell.
	private static JsonReaderState CellReaderState(DirectRowBinder binder) =>
		new(new JsonReaderOptions { MaxDepth = binder.CellMaxDepth });

	/// <summary>
	/// Reads the single cell of a scalar row. A null cell binds <c>default(T)</c> when <c>T</c> accepts null, as the
	/// serializer reads a root null. A null cell for a non-nullable value type and an unexpected token shape return
	/// false so the serializer keeps its own semantics for them (null into a non-nullable value type is its error, not ours).
	/// </summary>
	private static bool TryBindScalarDirect<T>(ref Utf8JsonReader reader, DirectBinderKind kind, out T? item, out bool incomplete)
	{
		item = default;
		incomplete = false;

		if (!reader.Read())
		{
			incomplete = true;
			return false;
		}

		var tokenType = reader.TokenType;
		if (tokenType == JsonTokenType.Null)
		{
			if (default(T) is not null)
				return false;
		}
		else if (tokenType == JsonTokenType.EndArray || !TryReadScalar(ref reader, kind, tokenType, out item))
			return false;

		if (!reader.Read())
		{
			item = default;
			incomplete = true;
			return false;
		}

		if (reader.TokenType == JsonTokenType.EndArray)
			return true;

		item = default;
		return false;
	}

	// Mirrors TryBindDirectValue, TryReadDirectValue and AssignDirectValue, so keep the four switches in sync when adding a new DirectBinderKind.
	private static bool TryReadScalar<T>(ref Utf8JsonReader reader, DirectBinderKind kind, JsonTokenType tokenType, out T? item)
	{
		item = default;

		if (IsEscapedNonStringValue(ref reader, kind))
			return false;

		switch (kind)
		{
			case DirectBinderKind.String:
				if (!TryGetString(ref reader, tokenType, out var stringValue))
					return false;
				item = (T)(object)stringValue!;
				return true;

			case DirectBinderKind.Bool:
				if (tokenType is not (JsonTokenType.True or JsonTokenType.False))
					return false;
				item = Lift<bool, T>(reader.GetBoolean());
				return true;

			case DirectBinderKind.Int32:
				if (tokenType != JsonTokenType.Number || !reader.TryGetInt32(out var int32Value))
					return false;
				item = Lift<int, T>(int32Value);
				return true;

			case DirectBinderKind.Int64:
				if (tokenType != JsonTokenType.Number || !reader.TryGetInt64(out var int64Value))
					return false;
				item = Lift<long, T>(int64Value);
				return true;

			case DirectBinderKind.Double:
				if (tokenType != JsonTokenType.Number || !reader.TryGetDouble(out var doubleValue))
					return false;
				item = Lift<double, T>(doubleValue);
				return true;

			case DirectBinderKind.Single:
				if (tokenType != JsonTokenType.Number || !reader.TryGetSingle(out var singleValue))
					return false;
				item = Lift<float, T>(singleValue);
				return true;

			case DirectBinderKind.Decimal:
				if (tokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var decimalValue))
					return false;
				item = Lift<decimal, T>(decimalValue);
				return true;

			case DirectBinderKind.DateTime:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTime(out var dateTimeValue))
					return false;
				item = Lift<DateTime, T>(dateTimeValue);
				return true;

			case DirectBinderKind.DateTimeOffset:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out var dateTimeOffsetValue))
					return false;
				item = Lift<DateTimeOffset, T>(dateTimeOffsetValue);
				return true;

			case DirectBinderKind.Guid:
				if (tokenType != JsonTokenType.String || !reader.TryGetGuid(out var guidValue))
					return false;
				item = Lift<Guid, T>(guidValue);
				return true;

			default:
				return false;
		}
	}

	/// <summary>Converts a parsed cell to <c>T</c>, which the classification guarantees is <c>TValue</c> or <c>TValue?</c>, without boxing.</summary>
	private static T Lift<TValue, T>(TValue value) where TValue : struct
	{
		if (typeof(T) == typeof(TValue))
			return Unsafe.As<TValue, T>(ref value);

		// TryClassifyScalar guarantees T is TValue or TValue?, so anything else is a classification bug.
		Debug.Assert(typeof(T) == typeof(TValue?), $"Expected T to be {typeof(TValue)} or {typeof(TValue?)}, but got {typeof(T)}.");
		TValue? nullable = value;
		return Unsafe.As<TValue?, T>(ref nullable);
	}

	// GetString throws InvalidOperationException for text that is not valid UTF-8 or an escape that is not valid UTF-16,
	// such as a lone surrogate. The serializer raises a JsonException with the path once the row falls back.
	private static bool TryGetString(ref Utf8JsonReader reader, JsonTokenType tokenType, out string? value)
	{
		value = null;

		if (tokenType != JsonTokenType.String)
			return false;

		try
		{
			value = reader.GetString();
			return true;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

#if !NETSTANDARD2_0
	// GetString decodes unescaped text with a decoder that throws on invalid UTF-8, so this decode returns the same string
	// and fails on the same input.
	private static bool TryDecodePlainString(ReadOnlySpan<byte> utf8, out string? value)
	{
		try
		{
			value = StrictUtf8.GetString(utf8);
			return true;
		}
		catch (DecoderFallbackException)
		{
			value = null;
			return false;
		}
	}

#endif
	// TryGetDateTime, TryGetDateTimeOffset and TryGetGuid unescape an escaped value before they parse it, and the unescape
	// throws InvalidOperationException for an escape that is not valid UTF-16, such as a lone surrogate. A valid date or
	// guid needs no escape, so an escaped one sends the row to the fallback, where the serializer reads it or raises a
	// JsonException with the path. A string cell decodes its own escapes through TryGetString.
	private static bool IsEscapedNonStringValue(ref Utf8JsonReader reader, DirectBinderKind kind) =>
		kind != DirectBinderKind.String && reader.ValueIsEscaped;

	// Mirrors TryReadScalar, TryReadDirectValue and AssignDirectValue, so keep the four switches in sync when adding a new DirectBinderKind.
	private static bool TryBindDirectValue(
		ref Utf8JsonReader reader,
		DirectBinderKind kind,
		JsonTokenType tokenType,
		JsonPropertyInfo property,
		Delegate? typedSetter,
		object instance)
	{
		if (IsEscapedNonStringValue(ref reader, kind))
			return false;

		switch (kind)
		{
			case DirectBinderKind.String:
				if (!TryGetString(ref reader, tokenType, out var stringValue))
					return false;
				Assign<string?>(typedSetter, property, instance, stringValue);
				return true;

			case DirectBinderKind.Bool:
				if (tokenType is not (JsonTokenType.True or JsonTokenType.False))
					return false;
				Assign(typedSetter, property, instance, reader.GetBoolean());
				return true;

			case DirectBinderKind.Int32:
				if (tokenType != JsonTokenType.Number || !reader.TryGetInt32(out var int32Value))
					return false;
				Assign(typedSetter, property, instance, int32Value);
				return true;

			case DirectBinderKind.Int64:
				if (tokenType != JsonTokenType.Number || !reader.TryGetInt64(out var int64Value))
					return false;
				Assign(typedSetter, property, instance, int64Value);
				return true;

			case DirectBinderKind.Double:
				if (tokenType != JsonTokenType.Number || !reader.TryGetDouble(out var doubleValue))
					return false;
				Assign(typedSetter, property, instance, doubleValue);
				return true;

			case DirectBinderKind.Single:
				if (tokenType != JsonTokenType.Number || !reader.TryGetSingle(out var singleValue))
					return false;
				Assign(typedSetter, property, instance, singleValue);
				return true;

			case DirectBinderKind.Decimal:
				if (tokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var decimalValue))
					return false;
				Assign(typedSetter, property, instance, decimalValue);
				return true;

			case DirectBinderKind.DateTime:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTime(out var dateTimeValue))
					return false;
				Assign(typedSetter, property, instance, dateTimeValue);
				return true;

			case DirectBinderKind.DateTimeOffset:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out var dateTimeOffsetValue))
					return false;
				Assign(typedSetter, property, instance, dateTimeOffsetValue);
				return true;

			case DirectBinderKind.Guid:
				if (tokenType != JsonTokenType.String || !reader.TryGetGuid(out var guidValue))
					return false;
				Assign(typedSetter, property, instance, guidValue);
				return true;

			default:
				return false;
		}
	}

	// Mirrors TryReadScalar and TryBindDirectValue, storing each value other than a string where AssignDirectValue reads it
	// back, so keep the four switches in sync when adding a new DirectBinderKind.
	private static bool TryReadDirectValue(
		ref Utf8JsonReader reader,
		DirectBinderKind kind,
		JsonTokenType tokenType,
		out DirectValue value)
	{
		value = default;

		if (IsEscapedNonStringValue(ref reader, kind))
			return false;

		switch (kind)
		{
			case DirectBinderKind.String:
				// TryDecodeStringCells decodes the string once the row is whole, so a retry after a fill repeats no allocation.
				return tokenType == JsonTokenType.String;

			case DirectBinderKind.Bool:
				if (tokenType is not (JsonTokenType.True or JsonTokenType.False))
					return false;
				value.Bool = reader.GetBoolean();
				return true;

			case DirectBinderKind.Int32:
				return tokenType == JsonTokenType.Number && reader.TryGetInt32(out value.Int32);

			case DirectBinderKind.Int64:
				return tokenType == JsonTokenType.Number && reader.TryGetInt64(out value.Int64);

			case DirectBinderKind.Double:
				return tokenType == JsonTokenType.Number && reader.TryGetDouble(out value.Double);

			case DirectBinderKind.Single:
				return tokenType == JsonTokenType.Number && reader.TryGetSingle(out value.Single);

			case DirectBinderKind.Decimal:
				return tokenType == JsonTokenType.Number && reader.TryGetDecimal(out value.Decimal);

			case DirectBinderKind.DateTime:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTime(out var dateTime))
					return false;
				// The ticks and kind alone drop the flag that places a local time in the first pass through the hour a
				// daylight saving change repeats, and ToBinary keeps it.
				value.Int64 = dateTime.ToBinary();
				return true;

			case DirectBinderKind.DateTimeOffset:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out var dateTimeOffset))
					return false;
				value.Int64 = dateTimeOffset.Ticks;
				value.OffsetMinutes = (short)dateTimeOffset.Offset.TotalMinutes;
				return true;

			case DirectBinderKind.Guid:
				return tokenType == JsonTokenType.String && reader.TryGetGuid(out value.Guid);

			default:
				return false;
		}
	}

	/// <summary>
	/// Reads an enum cell's number as the bits of the enum's underlying integer type, or returns false for any other token
	/// and for a number outside that type's range.
	/// </summary>
	private static bool TryReadEnumBits(ref Utf8JsonReader reader, JsonTokenType tokenType, TypeCode typeCode, out long bits)
	{
		bits = 0;

		// The serializer's enum converter reads a number through the underlying type's own TryGet method and returns it
		// whether or not the enum defines it. Number handling changes only how a string token reads, so a quoted number
		// goes to the fallback.
		if (tokenType != JsonTokenType.Number)
			return false;

		switch (typeCode)
		{
			case TypeCode.SByte when reader.TryGetSByte(out var sbyteValue):
				bits = sbyteValue;
				return true;

			case TypeCode.Byte when reader.TryGetByte(out var byteValue):
				bits = byteValue;
				return true;

			case TypeCode.Int16 when reader.TryGetInt16(out var int16Value):
				bits = int16Value;
				return true;

			case TypeCode.UInt16 when reader.TryGetUInt16(out var uint16Value):
				bits = uint16Value;
				return true;

			case TypeCode.Int32 when reader.TryGetInt32(out var int32Value):
				bits = int32Value;
				return true;

			case TypeCode.UInt32 when reader.TryGetUInt32(out var uint32Value):
				bits = uint32Value;
				return true;

			case TypeCode.Int64:
				return reader.TryGetInt64(out bits);

			case TypeCode.UInt64 when reader.TryGetUInt64(out var uint64Value):
				bits = unchecked((long)uint64Value);
				return true;

			default:
				return false;
		}
	}

	/// <summary>
	/// Assigns the bits <see cref="TryReadEnumBits"/> read through the column's typed setter, or as the enum through
	/// <see cref="JsonPropertyInfo.Set"/> where none was compiled.
	/// </summary>
	private static void AssignEnum(DirectRowBinder binder, int column, long bits, object instance)
	{
		if (binder.TypedSetters[column] is { } typedSetter)
		{
			((Action<object, long>)typedSetter)(instance, bits);
			return;
		}

		// Set unboxes its value as the property type, so the bits are boxed as the enum rather than as its integer type.
		// Enum.ToObject converts a ulong to long unchecked before it boxes, so the long overload gives a ulong enum the same value.
		binder.Properties[column].Set!(instance, Enum.ToObject(binder.EnumTypes[column]!, bits));
	}

	// Reads back the value TryReadDirectValue stored for the kind, or the string TryDecodeStringCells decoded, so keep the
	// four switches in sync when adding a new DirectBinderKind.
	private static void AssignDirectValue(
		DirectBinderKind kind,
		in DirectValue value,
		string? text,
		JsonPropertyInfo property,
		Delegate? typedSetter,
		object instance)
	{
		switch (kind)
		{
			case DirectBinderKind.String:
				Assign<string?>(typedSetter, property, instance, text);
				break;

			case DirectBinderKind.Bool:
				Assign(typedSetter, property, instance, value.Bool);
				break;

			case DirectBinderKind.Int32:
				Assign(typedSetter, property, instance, value.Int32);
				break;

			case DirectBinderKind.Int64:
				Assign(typedSetter, property, instance, value.Int64);
				break;

			case DirectBinderKind.Double:
				Assign(typedSetter, property, instance, value.Double);
				break;

			case DirectBinderKind.Single:
				Assign(typedSetter, property, instance, value.Single);
				break;

			case DirectBinderKind.Decimal:
				Assign(typedSetter, property, instance, value.Decimal);
				break;

			case DirectBinderKind.DateTime:
				Assign(typedSetter, property, instance, DateTime.FromBinary(value.Int64));
				break;

			case DirectBinderKind.DateTimeOffset:
				Assign(typedSetter, property, instance, new DateTimeOffset(value.Int64, TimeSpan.FromMinutes(value.OffsetMinutes)));
				break;

			case DirectBinderKind.Guid:
				Assign(typedSetter, property, instance, value.Guid);
				break;

			default:
				// TryAssignCells routes converter, string list and enum cells elsewhere and TryReadDirectValue rejects any
				// other kind, so only a kind stored there and missed here lands on this arm. Throwing fails such a row rather
				// than bind the property at its initializer.
				throw new InvalidOperationException($"No direct value is stored for {kind}.");
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Assign<TValue>(Delegate? typedSetter, JsonPropertyInfo property, object instance, TValue value)
	{
		if (typedSetter is Action<object, TValue> typed)
			typed(instance, value);
		else
			property.Set!(instance, value);
	}
}
