// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	private static bool TryReadNextRow<T>(
		ref ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		ColumnLayout layout,
		RowAssemblyBuffers buffers,
		RowMaterializationPlan<T> plan,
		out T? item,
		out bool reachedEnd)
	{
		item = default;
		reachedEnd = false;

		// Fast paths: eligible flat layouts bind cells straight off the reader, and scalar reads of the built-in
		// kinds read the one cell the same way. A row whose token shapes need the serializer's coercion or error
		// semantics falls through, per row, to assemble-and-deserialize below.
		var bindsDirect = buffers.IsScalar ? plan.ScalarKind is not null : layout.DirectBinder is not null;
		if (bindsDirect)
		{
			var savedState = state;
			var savedBuffer = buffer;

			var reader = new Utf8JsonReader(buffer, isFinalBlock, state);

			if (!reader.Read())
				return false;

			if (reader.TokenType == JsonTokenType.EndArray)
			{
				reachedEnd = true;
				state = reader.CurrentState;
				buffer = buffer.Slice(reader.Position);
				return true;
			}

			if (reader.TokenType == JsonTokenType.StartArray)
			{
				var bound = buffers.IsScalar
					? TryBindScalarDirect(ref reader, plan.ScalarKind!.Value, out item, out var incomplete)
					: TryBindRowDirect(ref reader, layout.DirectBinder!, out item, out incomplete);

				if (bound)
				{
					state = reader.CurrentState;
					buffer = buffer.Slice(reader.Position);
					return true;
				}

				if (incomplete)
				{
					state = savedState;
					buffer = savedBuffer;
					return false;
				}

				// state and buffer still hold the saved values, so TryAssembleNextRow re-reads this row from its StartArray.
			}
		}

		if (!TryAssembleNextRow(ref buffer, isFinalBlock, ref state, layout, buffers, out reachedEnd))
			return false;

		if (reachedEnd)
			return true;

		item = plan.TypeInfo is not null
			? JsonSerializer.Deserialize(buffers.RowBuffer.WrittenSpan, plan.TypeInfo)
			: JsonSerializer.Deserialize<T>(buffers.RowBuffer.WrittenSpan, plan.Options);

		return true;
	}

	/// <summary>
	/// Reads one row's cells directly off the reader and assigns them via cached
	/// <see cref="JsonPropertyInfo.Set"/> delegates. The reader must have just consumed the row's
	/// StartArray token. Returns false with <paramref name="incomplete"/> set when the buffer ends
	/// mid-row (caller re-reads with more data), or false with it unset when a cell's token shape
	/// requires the serializer's coercion or error semantics (caller falls back for this row).
	/// </summary>
	internal static bool TryBindRowDirect<T>(
		ref Utf8JsonReader reader,
		DirectRowBinder binder,
		out T? item,
		out bool incomplete)
	{
		item = default;
		incomplete = false;

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

			if (kinds[i] == DirectBinderKind.Converter)
			{
				if (!TryBindConverterCell(ref reader, tokenType, binder, i, instance, out incomplete))
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
	/// Deserializes one cell through its own contract and assigns it. A truncated cell reports incomplete; a cell
	/// the contract rejects returns false so the slow path re-reads the row and raises the canonical error.
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	private static bool TryBindConverterCell(
		ref Utf8JsonReader reader,
		JsonTokenType tokenType,
		DirectRowBinder binder,
		int index,
		object instance,
		out bool incomplete)
	{
		incomplete = false;

		// The serializer cannot tell a truncated value from an invalid one, so probe the extent first.
		var probe = reader;
		if (!probe.TrySkip())
		{
			incomplete = true;
			return false;
		}

		var cellTypeInfo = binder.CellTypeInfos[index]!;
		var elementTypeInfo = binder.ElementTypeInfos[index];
		object? value;

		try
		{
			if (elementTypeInfo is not null && tokenType != JsonTokenType.StartArray)
			{
				// ES|QL returns a single-valued multi-value field as a bare scalar; wrap it like the row path does.
				var list = (IList)cellTypeInfo.CreateObject!();
				_ = list.Add(JsonSerializer.Deserialize(ref reader, elementTypeInfo));
				value = list;
			}
			else
			{
				value = JsonSerializer.Deserialize(ref reader, cellTypeInfo);
			}
		}
		catch (JsonException)
		{
			return false;
		}

		binder.Properties[index].Set!(instance, value);
		return true;
	}

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

	// Mirrors TryBindDirectValue; keep both switches in sync when adding a new DirectBinderKind.
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

		// TryClassifyScalar guarantees T is TValue or TValue?; anything else is a classification bug.
		Debug.Assert(typeof(T) == typeof(TValue?), $"Expected T to be {typeof(TValue)} or {typeof(TValue?)}, but got {typeof(T)}.");
		TValue? nullable = value;
		return Unsafe.As<TValue?, T>(ref nullable);
	}

	// Mirrors TryReadScalar; keep both switches in sync when adding a new DirectBinderKind.
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Assign<TValue>(Delegate? typedSetter, JsonPropertyInfo property, object instance, TValue value)
	{
		if (typedSetter is Action<object, TValue> typed)
			typed(instance, value);
		else
			property.Set!(instance, value);
	}

	/// <summary>
	/// Parses the next row from the <c>values</c> array and assembles it into the row buffer
	/// (a JSON object, or a bare scalar value when <paramref name="buffers"/>.<see cref="RowAssemblyBuffers.IsScalar"/> is set)
	/// without deserializing. Returns <see langword="false"/> when more input is needed; state and buffer are
	/// restored so the caller can retry with more data.
	/// </summary>
	private static bool TryAssembleNextRow(
		ref ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		ColumnLayout layout,
		RowAssemblyBuffers buffers,
		out bool reachedEnd)
	{
		reachedEnd = false;

		var savedState = state;
		var savedBuffer = buffer;

		var reader = new Utf8JsonReader(buffer, isFinalBlock, state);

		if (!reader.Read())
			return false;

		if (reader.TokenType == JsonTokenType.EndArray)
		{
			reachedEnd = true;
			state = reader.CurrentState;
			buffer = buffer.Slice(reader.Position);
			return true;
		}

		if (reader.TokenType != JsonTokenType.StartArray)
		{
			state = reader.CurrentState;
			buffer = buffer.Slice(reader.Position);
			return false;
		}

		var materialized = buffers.IsScalar
			? TryCopyScalarValue(ref reader, buffer, buffers.RowBuffer)
			: TryMaterializeRow(ref reader, buffer, layout, buffers);

		if (!materialized)
		{
			state = savedState;
			buffer = savedBuffer;
			return false;
		}

		state = reader.CurrentState;
		buffer = buffer.Slice(reader.Position);
		return true;
	}

	private static bool TryMaterializeRow(ref Utf8JsonReader reader, in ReadOnlySequence<byte> source, ColumnLayout layout, RowAssemblyBuffers buffers) =>
		layout.BranchNodeCount == 0
			? TryMaterializeFlatRow(ref reader, source, layout, buffers.RowBuffer)
			: TryMaterializeNestedRow(ref reader, source, layout, buffers.RowBuffer, buffers.ValueBuffer!);

	/// <summary>
	/// Flat layouts list their leaves in column order, so every non-null cell is written into the row object as
	/// it is read; no per-cell scratch or regrouping is needed.
	/// </summary>
	private static bool TryMaterializeFlatRow(
		ref Utf8JsonReader reader,
		in ReadOnlySequence<byte> source,
		ColumnLayout layout,
		PooledBufferWriter rowBuffer)
	{
		var columnCount = layout.ColumnCount;
		rowBuffer.ResetWrittenCount();
		WriteRawByte(rowBuffer, (byte)'{');

		var colIndex = 0;
		var needsComma = false;
		while (true)
		{
			if (!reader.Read())
				return false;

			if (reader.TokenType == JsonTokenType.EndArray)
				break;

			if (colIndex >= columnCount)
				throw new JsonException($"ES|QL row contains more values than declared columns ({columnCount}).");

			if (reader.TokenType == JsonTokenType.Null)
			{
				colIndex++;
				continue;
			}

			var leaf = layout.LeafNodesByColumnIndex[colIndex];
			if (needsComma)
				WriteRawByte(rowBuffer, (byte)',');
			needsComma = true;

			WriteRawBytes(rowBuffer, leaf.PrefixBytes);

			// ES|QL returns a single-valued multi-value field as a bare scalar; the target property is a collection.
			var wrap = leaf.IsCollection && reader.TokenType != JsonTokenType.StartArray;
			if (wrap)
				WriteRawByte(rowBuffer, (byte)'[');
			if (!TryCopyCurrentValue(ref reader, source, rowBuffer))
				return false;
			if (wrap)
				WriteRawByte(rowBuffer, (byte)']');

			colIndex++;
		}

		if (colIndex < columnCount)
			throw new JsonException($"ES|QL row contains fewer values ({colIndex}) than declared columns ({columnCount}).");

		WriteRawByte(rowBuffer, (byte)'}');
		return true;
	}

	/// <summary>
	/// Nested layouts regroup cells into nested objects, so the cells are buffered in column order first and the
	/// row object is assembled from the tree afterwards.
	/// </summary>
	private static bool TryMaterializeNestedRow(
		ref Utf8JsonReader reader,
		in ReadOnlySequence<byte> source,
		ColumnLayout layout,
		PooledBufferWriter rowBuffer,
		PooledBufferWriter valueBuffer)
	{
		valueBuffer.ResetWrittenCount();

		var columnCount = layout.ColumnCount;

		ValueSlice[]? rentedSlices = null;
		var slices = columnCount <= 64
			? stackalloc ValueSlice[columnCount]
			: (rentedSlices = ArrayPool<ValueSlice>.Shared.Rent(columnCount)).AsSpan(0, columnCount);

		bool[]? rentedActiveBranches = null;
		var activeBranches = layout.BranchNodeCount switch
		{
			0 => [],
			<= 128 => stackalloc bool[layout.BranchNodeCount],
			_ => (rentedActiveBranches = ArrayPool<bool>.Shared.Rent(layout.BranchNodeCount)).AsSpan(0, layout.BranchNodeCount)
		};
		activeBranches.Clear();

		try
		{
			var colIndex = 0;
			while (true)
			{
				if (!reader.Read())
					return false;

				if (reader.TokenType == JsonTokenType.EndArray)
					break;

				if (colIndex >= columnCount)
					throw new JsonException($"ES|QL row contains more values than declared columns ({columnCount}).");

				if (reader.TokenType == JsonTokenType.Null)
				{
					slices[colIndex] = new ValueSlice(0, 0, JsonTokenType.Null, IsNull: true);
					colIndex++;
					continue;
				}

				var start = valueBuffer.WrittenCount;
				var firstToken = reader.TokenType;

				if (!TryCopyCurrentValue(ref reader, source, valueBuffer))
					return false;

				slices[colIndex] = new ValueSlice(start, valueBuffer.WrittenCount - start, firstToken, IsNull: false);
				MarkActiveBranches(layout.LeafNodesByColumnIndex[colIndex], activeBranches);
				colIndex++;
			}

			if (colIndex < columnCount)
				throw new JsonException($"ES|QL row contains fewer values ({colIndex}) than declared columns ({columnCount}).");

			rowBuffer.ResetWrittenCount();
			WriteRawByte(rowBuffer, (byte)'{');
			AssembleChildren(layout.Root.Children!, rowBuffer, valueBuffer.WrittenSpan, slices, activeBranches);
			WriteRawByte(rowBuffer, (byte)'}');

			return true;
		}
		finally
		{
			if (rentedSlices is not null)
				ArrayPool<ValueSlice>.Shared.Return(rentedSlices);
			if (rentedActiveBranches is not null)
				ArrayPool<bool>.Shared.Return(rentedActiveBranches);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void MarkActiveBranches(ColumnNode leafNode, Span<bool> activeBranches)
	{
		if (activeBranches.IsEmpty)
			return;

		var current = leafNode.Parent;
		while (current is not null)
		{
			if (current.BranchIndex >= 0)
				activeBranches[current.BranchIndex] = true;
			current = current.Parent;
		}
	}

	private static void AssembleChildren(
		List<ColumnNode> children,
		PooledBufferWriter buffer,
		ReadOnlySpan<byte> values,
		ReadOnlySpan<ValueSlice> slices,
		ReadOnlySpan<bool> activeBranches)
	{
		var needsComma = false;
		foreach (var child in children)
		{
			if (child.ColumnIndex >= 0)
			{
				var slice = slices[child.ColumnIndex];
				if (slice.IsNull)
					continue;

				if (needsComma)
					WriteRawByte(buffer, (byte)',');
				needsComma = true;

				WriteRawBytes(buffer, child.PrefixBytes);
				var raw = values.Slice(slice.Start, slice.Length);

				if (child.IsCollection && slice.FirstToken != JsonTokenType.StartArray)
				{
					WriteRawByte(buffer, (byte)'[');
					WriteRawBytes(buffer, raw);
					WriteRawByte(buffer, (byte)']');
				}
				else
				{
					WriteRawBytes(buffer, raw);
				}
			}
			else
			{
				if (child.Children is null)
					continue;
				if (child.BranchIndex >= 0 && !activeBranches[child.BranchIndex])
					continue;

				if (needsComma)
					WriteRawByte(buffer, (byte)',');
				needsComma = true;

				WriteRawBytes(buffer, child.PrefixBytes);
				WriteRawByte(buffer, (byte)'{');
				AssembleChildren(child.Children, buffer, values, slices, activeBranches);
				WriteRawByte(buffer, (byte)'}');
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void WriteRawByte(PooledBufferWriter buffer, byte value)
	{
		buffer.GetSpan(1)[0] = value;
		buffer.Advance(1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void WriteRawBytes(PooledBufferWriter buffer, ReadOnlySpan<byte> value)
	{
		value.CopyTo(buffer.GetSpan(value.Length));
		buffer.Advance(value.Length);
	}

	private static bool TryCopyScalarValue(ref Utf8JsonReader reader, in ReadOnlySequence<byte> source, PooledBufferWriter rowBuffer)
	{
		rowBuffer.ResetWrittenCount();

		if (!reader.Read())
			return false;

		if (!TryCopyCurrentValue(ref reader, source, rowBuffer))
			return false;

		if (!reader.Read())
			return false;

		if (reader.TokenType != JsonTokenType.EndArray)
			throw new JsonException("ES|QL row contains more values than declared columns (1).");

		return true;
	}

	/// <summary>
	/// Copies the current token's JSON text verbatim. The bytes already passed the reader's validation, so
	/// re-encoding them through a writer would only add a decode, a string allocation, and a second escape pass.
	/// </summary>
	private static bool TryCopyCurrentValue(ref Utf8JsonReader reader, in ReadOnlySequence<byte> source, PooledBufferWriter destination)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.String:
				WriteRawByte(destination, (byte)'"');
				CopyTokenValue(ref reader, destination);
				WriteRawByte(destination, (byte)'"');
				return true;

			case JsonTokenType.Number:
				CopyTokenValue(ref reader, destination);
				return true;

			case JsonTokenType.True:
				WriteRawBytes(destination, "true"u8);
				return true;

			case JsonTokenType.False:
				WriteRawBytes(destination, "false"u8);
				return true;

			case JsonTokenType.Null:
				WriteRawBytes(destination, "null"u8);
				return true;

			case JsonTokenType.StartArray:
			case JsonTokenType.StartObject:
				return TryCopyComplexValue(ref reader, source, destination);

			default:
				throw new JsonException($"Unexpected token {reader.TokenType} in ES|QL row value.");
		}
	}

	private static void CopyTokenValue(ref Utf8JsonReader reader, PooledBufferWriter destination)
	{
		if (!reader.HasValueSequence)
		{
			WriteRawBytes(destination, reader.ValueSpan);
			return;
		}

		var sequence = reader.ValueSequence;
		var length = checked((int)sequence.Length);
		sequence.CopyTo(destination.GetSpan(length));
		destination.Advance(length);
	}

	/// <summary>
	/// Skips the array or object and copies its source bytes, whitespace included, in one block. Returns false when
	/// the buffer ends inside the value; the caller then retries with more data.
	/// </summary>
	private static bool TryCopyComplexValue(ref Utf8JsonReader reader, in ReadOnlySequence<byte> source, PooledBufferWriter destination)
	{
		var start = reader.TokenStartIndex;
		if (!reader.TrySkip())
			return false;

		var raw = source.Slice(start, reader.BytesConsumed - start);
		var length = checked((int)raw.Length);
		raw.CopyTo(destination.GetSpan(length));
		destination.Advance(length);
		return true;
	}
}
