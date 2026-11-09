// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	/// <summary>A validated cell's extent in the source, and whether its bare scalar is read as <c>[value]</c>.</summary>
	private readonly record struct CellSpan(int Column, long Start, long Length, bool Wrap);

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
		var isRequired = binder.IsRequired;

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
				if (isRequired[i])
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
	/// Binds a row with converter cells in two passes. The first validates every cell and assigns nothing, so a row
	/// that falls back or ends mid-buffer has run no nested type's deserialization callback. The second assigns the
	/// cells in column order, the order the serializer's setters see.
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

		CellSpan[]? rentedSpans = null;
		var spans = columnCount <= 64
			? stackalloc CellSpan[columnCount]
			: (rentedSpans = ArrayPool<CellSpan>.Shared.Rent(columnCount)).AsSpan(0, columnCount);

		try
		{
			if (!TryValidateRow(ref reader, binder, spans, out var recorded, out incomplete))
				return false;

			var instance = binder.CreateObject();
			if (!TryAssignCells(source, binder, buffers, spans[..recorded], instance))
				return false;

			item = (T)instance;
			return true;
		}
		finally
		{
			if (rentedSpans is not null)
				ArrayPool<CellSpan>.Shared.Return(rentedSpans);
		}
	}

	/// <summary>
	/// Checks every cell's token shape, skips over every converter cell, and records the extent of each non-null cell.
	/// Returns false with <paramref name="incomplete"/> set when the buffer ends mid-row.
	/// </summary>
	private static bool TryValidateRow(ref Utf8JsonReader reader, DirectRowBinder binder, scoped Span<CellSpan> spans, out int recorded, out bool incomplete)
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
				if (binder.IsRequired[i])
					return false;
				continue;
			}

			var start = reader.TokenStartIndex;
			var wrap = binder.IsCollection[i] && tokenType != JsonTokenType.StartArray;

			if (kinds[i] == DirectBinderKind.Converter)
			{
				if (!reader.TrySkip())
				{
					incomplete = true;
					return false;
				}

				// The serializer rejects the assembled row once the cell nests, and a cell reader cannot enforce that
				// because it reads a depth limit of zero as the default.
				if (binder.CellMaxDepth < 1 && (wrap || tokenType is JsonTokenType.StartArray or JsonTokenType.StartObject))
					return false;
			}
			else if (!TryValidateDirectValue(ref reader, kinds[i], tokenType))
				return false;

			spans[recorded++] = new CellSpan(i, start, reader.BytesConsumed - start, wrap);
		}

		if (!reader.Read())
		{
			incomplete = true;
			return false;
		}

		// More cells than columns - the slow path raises the canonical JsonException.
		return reader.TokenType == JsonTokenType.EndArray;
	}

	private static bool TryAssignCells(
		in ReadOnlySequence<byte> source,
		DirectRowBinder binder,
		RowAssemblyBuffers buffers,
		ReadOnlySpan<CellSpan> spans,
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

			var cellReader = new Utf8JsonReader(source.Slice(span.Start, span.Length), isFinalBlock: true, CellReaderState(binder));
			_ = cellReader.Read();

			if (!TryBindDirectValue(ref cellReader, kind, cellReader.TokenType, binder.Properties[column], binder.TypedSetters[column], instance))
				return false;
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
		WriteRawByte(buffer, (byte)'[');
		foreach (var segment in value)
			WriteRawBytes(buffer, segment.Span);
		WriteRawByte(buffer, (byte)']');
		return buffer.WrittenSpan;
	}

	// The reader's depth limit takes precedence over the options' one inside Deserialize, and the assembled row
	// object sits one level above every cell.
	private static JsonReaderState CellReaderState(DirectRowBinder binder) =>
		new(new JsonReaderOptions { MaxDepth = binder.CellMaxDepth });

	/// <summary>
	/// Reads the single cell of a scalar row. Null cells and unexpected token shapes return false so the serializer
	/// keeps its own semantics for them (null into a non-nullable value type is its error, not ours).
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
		if (tokenType is JsonTokenType.Null or JsonTokenType.EndArray || !TryReadScalar(ref reader, kind, tokenType, out item))
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

	// Mirrors TryBindDirectValue and TryValidateDirectValue, so keep the three switches in sync when adding a new DirectBinderKind.
	private static bool TryReadScalar<T>(ref Utf8JsonReader reader, DirectBinderKind kind, JsonTokenType tokenType, out T? item)
	{
		item = default;

		switch (kind)
		{
			case DirectBinderKind.String:
				if (tokenType != JsonTokenType.String)
					return false;
				item = (T)(object)reader.GetString()!;
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

	// Mirrors TryReadScalar and TryValidateDirectValue, so keep the three switches in sync when adding a new DirectBinderKind.
	private static bool TryBindDirectValue(
		ref Utf8JsonReader reader,
		DirectBinderKind kind,
		JsonTokenType tokenType,
		JsonPropertyInfo property,
		Delegate? typedSetter,
		object instance)
	{
		switch (kind)
		{
			case DirectBinderKind.String:
				if (tokenType != JsonTokenType.String)
					return false;
				Assign<string?>(typedSetter, property, instance, reader.GetString());
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

	// Mirrors TryReadScalar and TryBindDirectValue without the assignment, so keep the three switches in sync when
	// adding a new DirectBinderKind.
	private static bool TryValidateDirectValue(ref Utf8JsonReader reader, DirectBinderKind kind, JsonTokenType tokenType) =>
		kind switch
		{
			DirectBinderKind.String => tokenType == JsonTokenType.String,
			DirectBinderKind.Bool => tokenType is JsonTokenType.True or JsonTokenType.False,
			DirectBinderKind.Int32 => tokenType == JsonTokenType.Number && reader.TryGetInt32(out _),
			DirectBinderKind.Int64 => tokenType == JsonTokenType.Number && reader.TryGetInt64(out _),
			DirectBinderKind.Double => tokenType == JsonTokenType.Number && reader.TryGetDouble(out _),
			DirectBinderKind.Single => tokenType == JsonTokenType.Number && reader.TryGetSingle(out _),
			DirectBinderKind.Decimal => tokenType == JsonTokenType.Number && reader.TryGetDecimal(out _),
			DirectBinderKind.DateTime => tokenType == JsonTokenType.String && reader.TryGetDateTime(out _),
			DirectBinderKind.DateTimeOffset => tokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out _),
			DirectBinderKind.Guid => tokenType == JsonTokenType.String && reader.TryGetGuid(out _),
			_ => false
		};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Assign<TValue>(Delegate? typedSetter, JsonPropertyInfo property, object instance, TValue value)
	{
		if (typedSetter is Action<object, TValue> typed)
			typed(instance, value);
		else
			property.Set!(instance, value);
	}
}
