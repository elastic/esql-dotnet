// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

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
					: TryBindRowDirect(ref reader, buffer, layout.DirectBinder!, buffers, out item, out incomplete);

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
			? TryCopyScalarValue(ref reader, buffer, buffers)
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

	[DoesNotReturn]
	private static void ThrowMoreValuesThanColumns(int columnCount) =>
		throw new JsonException($"ES|QL row contains more values than declared columns ({columnCount}).");

	[DoesNotReturn]
	private static void ThrowFewerValuesThanColumns(int colIndex, int columnCount) =>
		throw new JsonException($"ES|QL row contains fewer values ({colIndex}) than declared columns ({columnCount}).");

	private static bool TryMaterializeRow(ref Utf8JsonReader reader, in ReadOnlySequence<byte> source, ColumnLayout layout, RowAssemblyBuffers buffers) =>
		layout.BranchNodeCount == 0
			? TryMaterializeFlatRow(ref reader, source, layout, buffers.RowBuffer)
			: TryMaterializeNestedRow(ref reader, source, layout, buffers.RowBuffer, buffers.ValueBuffer);

	/// <summary>
	/// Flat layouts list their leaves in column order, so every non-null cell is written into the row object as
	/// it is read. No per-cell scratch or regrouping is needed.
	/// </summary>
	private static bool TryMaterializeFlatRow(
		ref Utf8JsonReader reader,
		in ReadOnlySequence<byte> source,
		ColumnLayout layout,
		PooledBufferWriter rowBuffer)
	{
		var columnCount = layout.ColumnCount;
		rowBuffer.ResetWrittenCount();
		rowBuffer.Write((byte)'{');

		var colIndex = 0;
		var needsComma = false;
		while (true)
		{
			if (!reader.Read())
				return false;

			if (reader.TokenType == JsonTokenType.EndArray)
				break;

			if (colIndex >= columnCount)
				ThrowMoreValuesThanColumns(columnCount);

			if (reader.TokenType == JsonTokenType.Null)
			{
				colIndex++;
				continue;
			}

			var leaf = layout.LeafNodesByColumnIndex[colIndex];
			if (needsComma)
				rowBuffer.Write((byte)',');
			needsComma = true;

			rowBuffer.Write(leaf.PrefixBytes);

			if (!TryCopyCell(ref reader, source, rowBuffer, leaf.IsCollection))
				return false;

			colIndex++;
		}

		if (colIndex < columnCount)
			ThrowFewerValuesThanColumns(colIndex, columnCount);

		rowBuffer.Write((byte)'}');
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
					ThrowMoreValuesThanColumns(columnCount);

				if (reader.TokenType == JsonTokenType.Null)
				{
					slices[colIndex] = new ValueSlice(0, 0, IsNull: true);
					colIndex++;
					continue;
				}

				var leaf = layout.LeafNodesByColumnIndex[colIndex];
				var start = valueBuffer.WrittenCount;

				if (!TryCopyCell(ref reader, source, valueBuffer, leaf.IsCollection))
					return false;

				slices[colIndex] = new ValueSlice(start, valueBuffer.WrittenCount - start, IsNull: false);
				MarkActiveBranches(leaf, activeBranches);
				colIndex++;
			}

			if (colIndex < columnCount)
				ThrowFewerValuesThanColumns(colIndex, columnCount);

			rowBuffer.ResetWrittenCount();
			rowBuffer.Write((byte)'{');
			AssembleChildren(layout.Root.Children!, rowBuffer, valueBuffer.WrittenSpan, slices, activeBranches);
			rowBuffer.Write((byte)'}');

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
					buffer.Write((byte)',');
				needsComma = true;

				buffer.Write(child.PrefixBytes);
				buffer.Write(values.Slice(slice.Start, slice.Length));
			}
			else
			{
				if (child.Children is null)
					continue;
				if (child.BranchIndex >= 0 && !activeBranches[child.BranchIndex])
					continue;

				if (needsComma)
					buffer.Write((byte)',');
				needsComma = true;

				buffer.Write(child.PrefixBytes);
				buffer.Write((byte)'{');
				AssembleChildren(child.Children, buffer, values, slices, activeBranches);
				buffer.Write((byte)'}');
			}
		}
	}

	private static bool TryCopyScalarValue(ref Utf8JsonReader reader, in ReadOnlySequence<byte> source, RowAssemblyBuffers buffers)
	{
		var rowBuffer = buffers.RowBuffer;
		rowBuffer.ResetWrittenCount();

		if (!reader.Read())
			return false;

		if (!TryCopyCell(ref reader, source, rowBuffer, buffers.WrapScalarInArray))
			return false;

		if (!reader.Read())
			return false;

		if (reader.TokenType != JsonTokenType.EndArray)
			ThrowMoreValuesThanColumns(1);

		return true;
	}

	/// <summary>Copies the current cell, writing a bare value for a collection target as <c>[value]</c>.</summary>
	private static bool TryCopyCell(ref Utf8JsonReader reader, in ReadOnlySequence<byte> source, PooledBufferWriter destination, bool collectionTarget)
	{
		// ES|QL returns a single-valued multi-value field as a bare scalar, but a collection target reads a JSON array.
		// A null cell stays null.
		var wrap = collectionTarget && reader.TokenType is not (JsonTokenType.StartArray or JsonTokenType.Null);
		if (wrap)
			destination.Write((byte)'[');

		if (!TryCopyCurrentValue(ref reader, source, destination))
			return false;

		if (wrap)
			destination.Write((byte)']');

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
				destination.Write((byte)'"');
				CopyTokenValue(ref reader, destination);
				destination.Write((byte)'"');
				return true;

			case JsonTokenType.Number:
				CopyTokenValue(ref reader, destination);
				return true;

			case JsonTokenType.True:
				destination.Write("true"u8);
				return true;

			case JsonTokenType.False:
				destination.Write("false"u8);
				return true;

			case JsonTokenType.Null:
				destination.Write("null"u8);
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
		if (reader.HasValueSequence)
			destination.Write(reader.ValueSequence);
		else
			destination.Write(reader.ValueSpan);
	}

	/// <summary>
	/// Skips the array or object and copies its source bytes, whitespace included, in one block. Returns false when
	/// the buffer ends inside the value, and the caller then retries with more data.
	/// </summary>
	private static bool TryCopyComplexValue(ref Utf8JsonReader reader, in ReadOnlySequence<byte> source, PooledBufferWriter destination)
	{
		var start = reader.TokenStartIndex;
		if (!reader.TrySkip())
			return false;

		destination.Write(source.Slice(start, reader.BytesConsumed - start));
		return true;
	}
}
