// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	/// <summary>
	/// A validated cell's extent in the source, whether its bare scalar is read as <c>[value]</c>, and the value a built-in cell
	/// other than a string parsed to.
	/// </summary>
	private readonly record struct CellSpan(int Column, long Start, long Length, bool Wrap, DirectValue Value);

	/// <summary>The parsed value of a built-in cell, in the field its <see cref="DirectBinderKind"/> selects.</summary>
	/// <remarks>
	/// The fields overlap, so the struct stays 16 bytes and unmanaged and a stackalloc'd row of <see cref="CellSpan"/>
	/// can carry each built-in cell other than a string from the validating pass to the assigning one without a second
	/// parse. A string is a reference, which cannot overlap the value fields and would make the row managed, so it travels
	/// in the per-enumeration scratch instead. Only sequential-layout fields overlap, because an auto-layout field such as
	/// DateTime inside an explicit layout is not guaranteed to load on every runtime the netstandard2.0 build serves. A
	/// date lives in <see cref="Int64"/>, so offset 8 is free for the offset of a DateTimeOffset whenever a date is the
	/// live kind.
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

		if (binder.ConverterCellCount > 0)
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
	/// Binds a row with converter cells in three steps. The validating pass checks every cell's shape, parses each built-in
	/// cell other than a string, and assigns nothing, so a row that fails it or ends mid-buffer has run no nested type's
	/// deserialization callback. Once that pass has seen the closing bracket, each string cell decodes into the scratch, and
	/// a string the reader cannot decode sends the row to the fallback before any converter cell runs. The assigning pass
	/// then assigns the cells in column order, the order the serializer's setters see. A cell that only the assigning pass
	/// decodes can still send the row to the fallback (a converter cell its contract rejects, a null converter result for a
	/// property that does not accept null) after the converter cells in that cell or before it have run their callbacks.
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
			if (!TryDecodeStringCells(source, binder, spans[..recorded], strings))
				return false;

			var instance = binder.CreateObject();
			if (!TryAssignCells(source, binder, buffers, spans[..recorded], strings, instance))
				return false;

			item = (T)instance;
			return true;
		}
		finally
		{
			// A string left in the scratch stays reachable until a later row overwrites it or the enumeration ends.
			Array.Clear(strings, 0, columnCount);
			if (rentedSpans is not null)
				ArrayPool<CellSpan>.Shared.Return(rentedSpans);
		}
	}

	/// <summary>
	/// Checks every cell's token shape, skips over every converter cell, and records the extent of each non-null cell
	/// with the parsed value of each built-in cell other than a string. Returns false with <paramref name="incomplete"/>
	/// set when the buffer ends mid-row.
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
			var value = default(DirectValue);

			if (kinds[i] == DirectBinderKind.Converter)
			{
				if (!reader.TrySkip())
				{
					incomplete = true;
					return false;
				}
			}
			else if (!TryReadDirectValue(ref reader, kinds[i], tokenType, out value))
				return false;

			spans[recorded++] = new CellSpan(i, start, reader.BytesConsumed - start, wrap, value);
		}

		if (!reader.Read())
		{
			incomplete = true;
			return false;
		}

		// More cells than columns - the slow path raises the canonical JsonException.
		return reader.TokenType == JsonTokenType.EndArray;
	}

	private static bool TryDecodeStringCells(
		in ReadOnlySequence<byte> source,
		DirectRowBinder binder,
		ReadOnlySpan<CellSpan> spans,
		string?[] strings)
	{
		foreach (var span in spans)
		{
			if (binder.Kinds[span.Column] != DirectBinderKind.String)
				continue;

			var cellReader = new Utf8JsonReader(source.Slice(span.Start, span.Length), isFinalBlock: true, CellReaderState(binder));
			_ = cellReader.Read();

			if (!TryGetString(ref cellReader, cellReader.TokenType, out strings[span.Column]))
				return false;
		}

		return true;
	}

	private static bool TryAssignCells(
		in ReadOnlySequence<byte> source,
		DirectRowBinder binder,
		RowAssemblyBuffers buffers,
		ReadOnlySpan<CellSpan> spans,
		string?[] strings,
		object instance)
	{
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
		var slice = source.Slice(span.Start, span.Length);
		var cellTypeInfo = binder.CellTypeInfos[span.Column]!;
		var property = binder.Properties[span.Column];

		// ES|QL returns a single-valued multi-value field as a bare scalar, and the row path rewrites it as [value].
		var cellReader = span.Wrap
			? new Utf8JsonReader(WrapInArray(slice, buffers.RowBuffer), isFinalBlock: true, CellReaderState(binder))
			: new Utf8JsonReader(slice, isFinalBlock: true, CellReaderState(binder));

		object? value;
		try
		{
			_ = cellReader.Read();
			value = JsonSerializer.Deserialize(ref cellReader, cellTypeInfo);
		}
		catch (JsonException)
		{
			return false;
		}

		// The serializer raises its nullability error for this value, and the direct path must not accept what it rejects.
		if (value is null && cellTypeInfo.Options.RespectNullableAnnotations && !property.IsSetNullable)
			return false;

		property.Set!(instance, value);
		return true;
	}

	private static ReadOnlySpan<byte> WrapInArray(in ReadOnlySequence<byte> value, PooledBufferWriter buffer)
	{
		buffer.ResetWrittenCount();
		buffer.Write((byte)'[');
		buffer.Write(value);
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
				// TryAssignCells routes converter cells elsewhere and TryReadDirectValue rejects any other kind, so only a
				// kind stored there and missed here lands on this arm. Throwing fails such a row rather than bind the
				// property at its initializer.
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
