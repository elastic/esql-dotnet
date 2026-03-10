// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	private static readonly JsonWriterOptions SkipValidationWriterOptions = new() { SkipValidation = true };

	/// <summary>
	/// Materializes each row of an ES|QL response from a <see cref="Stream"/> as an instance of <typeparamref name="T"/>.
	/// </summary>
	public async IAsyncEnumerable<T> ReadRowsAsync<T>(
		Stream stream,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await using var ownedPipeReader = CreateOwnedPipeReader(stream);

		await foreach (var item in ReadRowsAsync<T>(ownedPipeReader.Reader, cancellationToken)
						   .ConfigureAwait(false))
			yield return item;
	}

	/// <summary>
	/// Materializes each row of an ES|QL response from a <see cref="PipeReader"/> as an instance of <typeparamref name="T"/>.
	/// </summary>
	public async IAsyncEnumerable<T> ReadRowsAsync<T>(
		PipeReader pipeReader,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var (columns, readerState, consumed, examined, _, _) =
			await ReadColumnsFromPipeAsync(pipeReader, cancellationToken)
				.ConfigureAwait(false);

		pipeReader.AdvanceTo(consumed, examined);

		(readerState, _, _) = await AdvanceToValuesArrayFromPipeAsync(pipeReader, readerState, cancellationToken).ConfigureAwait(false);

		var layout = GetColumnLayout<T>(columns);

		await foreach (var item in StreamRowsAsync<T>(pipeReader, readerState, columns, layout, Options, cancellationToken)
						   .ConfigureAwait(false))
			yield return item;
	}

	/// <summary>
	/// Materializes each row of an ES|QL response from a <see cref="Stream"/> as an instance of <typeparamref name="T"/>.
	/// </summary>
	public IEnumerable<T> ReadRows<T>(Stream stream)
	{
		using var syncBuffer = new SyncStreamBuffer(stream);

		var (columns, readerState, consumed, _, _) = ReadColumnsFromStream(syncBuffer);
		syncBuffer.AdvanceTo(consumed);

		(readerState, _, _) = AdvanceToValuesArrayFromStream(syncBuffer, readerState);

		var layout = GetColumnLayout<T>(columns);

		foreach (var item in StreamRows<T>(syncBuffer, readerState, columns, layout, Options))
			yield return item;
	}

	/// <summary>
	/// Streams rows from an ES|QL async query response, progressively capturing <c>id</c> and <c>is_running</c> metadata.
	/// <para>
	/// Metadata properties are captured whenever encountered during JSON parsing. Because JSON does not guarantee
	/// property order, <c>id</c>/<c>is_running</c> may appear before <c>columns</c>, between <c>columns</c>
	/// and <c>values</c>, or after <c>values</c>. As a result, <see cref="EsqlAsyncResponse{T}.Metadata"/> is
	/// <b>not guaranteed to be fully populated</b> until <see cref="EsqlAsyncResponse{T}.Rows"/> has been fully consumed.
	/// </para>
	/// </summary>
	public Task<EsqlAsyncResponse<T>> ReadRowsWithMetadataAsync<T>(
		Stream stream,
		CancellationToken cancellationToken = default) =>
		ReadRowsWithMetadataAsync<T>(CreatePipeReader(stream), cancellationToken);

	/// <inheritdoc cref="ReadRowsWithMetadataAsync{T}(Stream, CancellationToken)"/>
	public Task<EsqlAsyncResponse<T>> ReadRowsWithMetadataAsync<T>(
		PipeReader pipeReader,
		CancellationToken cancellationToken = default)
	{
		var result = new EsqlAsyncResponse<T>();
		result.Rows = ReadRowsWithMetadataCore<T>(pipeReader, result, cancellationToken);
		return Task.FromResult(result);
	}

	/// <summary>
	/// Streams rows from an ES|QL response synchronously, progressively capturing <c>id</c> and <c>is_running</c> metadata.
	/// </summary>
	public EsqlResponse<T> ReadRowsWithMetadata<T>(Stream stream)
	{
		var result = new EsqlResponse<T>();
		result.Rows = ReadRowsWithMetadataCore<T>(stream, result);
		return result;
	}

	private async IAsyncEnumerable<T> ReadRowsWithMetadataCore<T>(
		PipeReader pipeReader,
		EsqlAsyncResponse<T> result,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		try
		{
			var (columns, readerState, consumed, examined, id, isRunning) =
				await ReadColumnsFromPipeAsync(pipeReader, cancellationToken)
					.ConfigureAwait(false);

			result.SetId(id);
			if (isRunning.HasValue)
				result.SetIsRunning(isRunning.Value);

			pipeReader.AdvanceTo(consumed, examined);

			(readerState, id, isRunning) = await AdvanceToValuesArrayFromPipeAsync(
				pipeReader, readerState, cancellationToken).ConfigureAwait(false);

			if (id != null)
				result.SetId(id);
			if (isRunning.HasValue)
				result.SetIsRunning(isRunning.Value);

			var layout = GetColumnLayout<T>(columns);

			await foreach (var item in StreamRowsAsync<T>(pipeReader, readerState, columns, layout, Options, cancellationToken)
				.ConfigureAwait(false))
				yield return item;

			await ScanRemainingMetadataAsync(pipeReader, readerState, result, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}
	}

	private IEnumerable<T> ReadRowsWithMetadataCore<T>(
		Stream stream,
		EsqlResponse<T> result)
	{
		using var syncBuffer = new SyncStreamBuffer(stream);

		var (columns, readerState, consumed, id, isRunning) = ReadColumnsFromStream(syncBuffer);

		result.SetId(id);
		if (isRunning.HasValue)
			result.SetIsRunning(isRunning.Value);

		syncBuffer.AdvanceTo(consumed);

		(readerState, id, isRunning) = AdvanceToValuesArrayFromStream(syncBuffer, readerState);

		if (id is not null)
			result.SetId(id);
		if (isRunning.HasValue)
			result.SetIsRunning(isRunning.Value);

		var layout = GetColumnLayout<T>(columns);

		foreach (var item in StreamRows<T>(syncBuffer, readerState, columns, layout, Options))
			yield return item;

		ScanRemainingMetadata(syncBuffer, readerState, result);
	}

	/// <summary>Streams rows one at a time from the <c>values</c> array.</summary>
	private static async IAsyncEnumerable<T> StreamRowsAsync<T>(
		PipeReader pipeReader,
		JsonReaderState readerState,
		ColumnInfo[] columns,
		ColumnLayout layout,
		JsonSerializerOptions options,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var estimatedRowSize = Math.Max(256, columns.Length * 32);
		var rowBuffer = new ArrayBufferWriter<byte>(estimatedRowSize);
		var typeInfo = TryResolveTypeInfo<T>(options);

		var isScalar = columns.Length == 1 && IsPrimitiveJsonType(typeof(T));
		var valueBuffer = isScalar ? null : new ArrayBufferWriter<byte>(estimatedRowSize);
		await using var valueWriter = isScalar ? null : new Utf8JsonWriter(valueBuffer!, SkipValidationWriterOptions);
		await using var scalarWriter = isScalar ? new Utf8JsonWriter(rowBuffer, SkipValidationWriterOptions) : null;

		var done = false;

		while (!done)
		{
			var result = await pipeReader.ReadAsync(cancellationToken)
				.ConfigureAwait(false);

			var buffer = result.Buffer;
			var isFinalBlock = result.IsCompleted;
			var reachedEnd = false;

			while (TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter, typeInfo, options, out var item, out reachedEnd))
			{
				if (reachedEnd)
				{
					done = true;
					break;
				}

				yield return item!;
			}

			if (reachedEnd)
				done = true;

			pipeReader.AdvanceTo(buffer.Start, buffer.End);

			if (result.IsCompleted)
				break;
		}
	}

	private static IEnumerable<T> StreamRows<T>(
		SyncStreamBuffer syncBuffer,
		JsonReaderState readerState,
		ColumnInfo[] columns,
		ColumnLayout layout,
		JsonSerializerOptions options)
	{
		var estimatedRowSize = Math.Max(256, columns.Length * 32);
		var rowBuffer = new ArrayBufferWriter<byte>(estimatedRowSize);
		var typeInfo = TryResolveTypeInfo<T>(options);

		var isScalar = columns.Length == 1 && IsPrimitiveJsonType(typeof(T));
		var valueBuffer = isScalar ? null : new ArrayBufferWriter<byte>(estimatedRowSize);
		using var valueWriter = isScalar ? null : new Utf8JsonWriter(valueBuffer!, SkipValidationWriterOptions);
		using var scalarWriter = isScalar ? new Utf8JsonWriter(rowBuffer, SkipValidationWriterOptions) : null;

		var done = false;

		while (!done)
		{
			if (!syncBuffer.Read() && syncBuffer.IsCompleted && syncBuffer.Buffer.IsEmpty)
				break;

			var buffer = syncBuffer.Buffer;
			var isFinalBlock = syncBuffer.IsCompleted;
			var reachedEnd = false;

			while (TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter, typeInfo, options, out var item, out reachedEnd))
			{
				if (reachedEnd)
				{
					done = true;
					break;
				}

				yield return item!;
			}

			if (reachedEnd)
				done = true;

			syncBuffer.AdvanceTo(buffer.Start, buffer.End);
		}
	}

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	private static bool TryReadNextRow<T>(
		ref ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		ColumnLayout layout,
		ArrayBufferWriter<byte> rowBuffer,
		ArrayBufferWriter<byte>? valueBuffer,
		Utf8JsonWriter? valueWriter,
		Utf8JsonWriter? scalarWriter,
		JsonTypeInfo<T>? typeInfo,
		JsonSerializerOptions options,
		out T? item,
		out bool reachedEnd)
	{
		item = default;
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

		if (scalarWriter is not null)
		{
			if (!TryWriteScalarValue(ref reader, rowBuffer, scalarWriter))
			{
				state = savedState;
				buffer = savedBuffer;
				return false;
			}
		}
		else if (valueBuffer is null || valueWriter is null || !TryMaterializeRow(ref reader, layout, rowBuffer, valueBuffer, valueWriter))
		{
			state = savedState;
			buffer = savedBuffer;
			return false;
		}

		item = typeInfo is not null
			? JsonSerializer.Deserialize(rowBuffer.WrittenSpan, typeInfo)
			: JsonSerializer.Deserialize<T>(rowBuffer.WrittenSpan, options);

		state = reader.CurrentState;
		buffer = buffer.Slice(reader.Position);
		return true;
	}

	private static bool TryMaterializeRow(
		ref Utf8JsonReader reader,
		ColumnLayout layout,
		ArrayBufferWriter<byte> rowBuffer,
		ArrayBufferWriter<byte> valueBuffer,
		Utf8JsonWriter valueWriter)
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

				valueWriter.Reset();
				if (!TryWriteCurrentValue(ref reader, valueWriter))
					return false;
				valueWriter.Flush();

				var length = valueBuffer.WrittenCount - start;
				slices[colIndex] = new ValueSlice(start, length, firstToken, IsNull: false);
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
		ArrayBufferWriter<byte> buffer,
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
	private static void WriteRawByte(ArrayBufferWriter<byte> buffer, byte value)
	{
		buffer.GetSpan(1)[0] = value;
		buffer.Advance(1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void WriteRawBytes(ArrayBufferWriter<byte> buffer, ReadOnlySpan<byte> value)
	{
		value.CopyTo(buffer.GetSpan(value.Length));
		buffer.Advance(value.Length);
	}

	private static bool IsPrimitiveJsonType(Type type)
	{
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t.IsPrimitive || t == typeof(decimal) || t == typeof(string) || t.IsEnum;
	}

	private static bool TryWriteScalarValue(ref Utf8JsonReader reader, ArrayBufferWriter<byte> buffer, Utf8JsonWriter writer)
	{
		buffer.ResetWrittenCount();
		writer.Reset();

		if (!reader.Read())
			return false;

		if (!TryWriteCurrentValue(ref reader, writer))
			return false;

		if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
			return false;

		writer.Flush();
		return true;
	}

	private static bool TryWriteCurrentValue(
		ref Utf8JsonReader reader,
		Utf8JsonWriter writer)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.String:
				writer.WriteStringValue(reader.HasValueSequence
					? reader.ValueSequence.ToArray()
					: reader.ValueSpan);
				return true;

			case JsonTokenType.Number:
				writer.WriteRawValue(reader.HasValueSequence
					? reader.ValueSequence.ToArray()
					: reader.ValueSpan, skipInputValidation: true);
				return true;

			case JsonTokenType.True:
				writer.WriteBooleanValue(true);
				return true;

			case JsonTokenType.False:
				writer.WriteBooleanValue(false);
				return true;

			case JsonTokenType.Null:
				writer.WriteNullValue();
				return true;

			case JsonTokenType.StartArray:
			case JsonTokenType.StartObject:
				return TryWriteComplexValue(ref reader, writer);

			default:
				throw new JsonException($"Unexpected token {reader.TokenType} in ES|QL row value.");
		}
	}

	private static bool TryWriteComplexValue(
		ref Utf8JsonReader reader,
		Utf8JsonWriter writer)
	{
		var depth = reader.CurrentDepth;

		if (reader.TokenType == JsonTokenType.StartArray)
			writer.WriteStartArray();
		else
			writer.WriteStartObject();

		while (true)
		{
			if (!reader.Read())
				return false;

			if (reader.CurrentDepth <= depth)
			{
				if (reader.TokenType == JsonTokenType.EndArray)
					writer.WriteEndArray();
				else
					writer.WriteEndObject();
				break;
			}

			switch (reader.TokenType)
			{
				case JsonTokenType.PropertyName:
					writer.WritePropertyName(reader.HasValueSequence
						? reader.ValueSequence.ToArray()
						: reader.ValueSpan
					);
					break;
				case JsonTokenType.StartObject:
					writer.WriteStartObject();
					break;
				case JsonTokenType.EndObject:
					writer.WriteEndObject();
					break;
				case JsonTokenType.StartArray:
					writer.WriteStartArray();
					break;
				case JsonTokenType.EndArray:
					writer.WriteEndArray();
					break;
				default:
					if (!TryWriteCurrentValue(ref reader, writer))
						return false;
					break;
			}
		}

		return true;
	}

}
