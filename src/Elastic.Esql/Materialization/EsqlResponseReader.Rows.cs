// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	/// <summary>
	/// Materializes each row of an ES|QL response from a <see cref="Stream"/> as an instance of <typeparamref name="T"/>.
	/// </summary>
	public async IAsyncEnumerable<T> ReadRowsAsync<T>(
		Stream stream,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var pipeReader = CreatePipeReader(stream);

		try
		{
			await foreach (var item in ReadRowsAsync<T>(pipeReader, cancellationToken)
							   .ConfigureAwait(false))
				yield return item;
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}
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

	/// <summary>
	/// After all rows have been consumed, scans remaining top-level JSON properties
	/// for <c>id</c> and <c>is_running</c> that may appear after the <c>values</c> array.
	/// </summary>
	private static async Task ScanRemainingMetadataAsync<T>(
		PipeReader pipeReader,
		JsonReaderState state,
		EsqlAsyncResponse<T> result,
		CancellationToken ct)
	{
		string? id = null;
		bool? isRunning = null;

		while (true)
		{
			var pipeResult = await pipeReader.ReadAsync(ct).ConfigureAwait(false);
			var buffer = pipeResult.Buffer;

			if (TryScanRemainingProperties(buffer, pipeResult.IsCompleted, ref state, out var consumed, ref id, ref isRunning, out _))
			{
				pipeReader.AdvanceTo(consumed, buffer.End);
				break;
			}

			pipeReader.AdvanceTo(consumed, buffer.End);

			if (pipeResult.IsCompleted)
				break;
		}

		if (id is not null)
			result.SetId(id);
		if (isRunning.HasValue)
			result.SetIsRunning(isRunning.Value);
	}

	private static void ScanRemainingMetadata<T>(
		SyncStreamBuffer syncBuffer,
		JsonReaderState state,
		EsqlResponse<T> result)
	{
		string? id = null;
		bool? isRunning = null;

		while (syncBuffer.Read() || !syncBuffer.IsCompleted)
		{
			var buffer = syncBuffer.Buffer;
			if (buffer.IsEmpty && syncBuffer.IsCompleted)
				break;

			if (TryScanRemainingProperties(buffer, syncBuffer.IsCompleted, ref state, out var consumed, ref id, ref isRunning, out _))
			{
				syncBuffer.AdvanceTo(consumed, buffer.End);
				break;
			}

			syncBuffer.AdvanceTo(consumed, buffer.End);

			if (syncBuffer.IsCompleted)
				break;
		}

		if (id is not null)
			result.SetId(id);
		if (isRunning.HasValue)
			result.SetIsRunning(isRunning.Value);
	}

	private static bool TryScanRemainingProperties(
		ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		out SequencePosition consumed,
		ref string? id,
		ref bool? isRunning,
		out bool reachedEnd)
	{
		reachedEnd = false;
		var reader = new Utf8JsonReader(buffer, isFinalBlock, state);

		while (reader.Read())
		{
			if (reader.CurrentDepth == 0 && reader.TokenType == JsonTokenType.EndObject)
			{
				reachedEnd = true;
				state = reader.CurrentState;
				consumed = reader.Position;
				return true;
			}

			if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName)
				continue;

			if (reader.ValueTextEquals("id"u8))
			{
				if (!reader.Read())
					break;
				id = reader.GetString();
				continue;
			}

			if (reader.ValueTextEquals("is_running"u8))
			{
				if (!reader.Read())
					break;
				isRunning = reader.GetBoolean();
				continue;
			}

			if (!reader.TrySkip())
				break;
		}

		state = reader.CurrentState;
		consumed = reader.Position;
		return false;
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
		var rowBuffer = new ArrayBufferWriter<byte>(256);
		var valueBuffer = layout.HasNestedObjects ? new ArrayBufferWriter<byte>(256) : null;
		var done = false;

		while (!done)
		{
			var result = await pipeReader.ReadAsync(cancellationToken)
				.ConfigureAwait(false);

			var buffer = result.Buffer;
			var isFinalBlock = result.IsCompleted;
			var reachedEnd = false;

			while (TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, columns, layout, rowBuffer, valueBuffer, options, out var item, out reachedEnd))
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
		var rowBuffer = new ArrayBufferWriter<byte>(256);
		var valueBuffer = layout.HasNestedObjects ? new ArrayBufferWriter<byte>(256) : null;
		var done = false;

		while (!done)
		{
			if (!syncBuffer.Read() && syncBuffer.IsCompleted && syncBuffer.Buffer.IsEmpty)
				break;

			var buffer = syncBuffer.Buffer;
			var isFinalBlock = syncBuffer.IsCompleted;
			var reachedEnd = false;

			while (TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, columns, layout, rowBuffer, valueBuffer, options, out var item, out reachedEnd))
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
		ReadOnlySpan<ColumnInfo> columns,
		ColumnLayout layout,
		ArrayBufferWriter<byte> rowBuffer,
		ArrayBufferWriter<byte>? valueBuffer,
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

		if (columns.Length == 1 && IsPrimitiveJsonType(typeof(T)))
		{
			if (!TryWriteScalarValue(ref reader, rowBuffer))
			{
				state = savedState;
				buffer = savedBuffer;
				return false;
			}
		}
		else if (layout.HasNestedObjects)
		{
			if (!TryWriteRowAsNestedObject(ref reader, columns, layout, rowBuffer, valueBuffer!))
			{
				state = savedState;
				buffer = savedBuffer;
				return false;
			}
		}
		else if (!TryWriteRowAsObject(ref reader, columns, layout.CollectionColumns, rowBuffer))
		{
			state = savedState;
			buffer = savedBuffer;
			return false;
		}

		item = JsonSerializer.Deserialize<T>(rowBuffer.WrittenSpan, options);

		state = reader.CurrentState;
		buffer = buffer.Slice(reader.Position);
		return true;
	}

	private static bool TryWriteRowAsObject(
		ref Utf8JsonReader reader,
		ReadOnlySpan<ColumnInfo> columns,
		bool[]? collectionColumns,
		ArrayBufferWriter<byte> buffer)
	{
		buffer.ResetWrittenCount();

		using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
		{
			SkipValidation = true
		});

		writer.WriteStartObject();

		var colIndex = 0;

		while (true)
		{
			if (!reader.Read())
				return false;

			if (reader.TokenType == JsonTokenType.EndArray)
				break;

			if (colIndex >= columns.Length)
				throw new JsonException($"ES|QL row contains more values than declared columns ({columns.Length}).");

			if (reader.TokenType == JsonTokenType.Null)
			{
				colIndex++;
				continue;
			}

			writer.WritePropertyName(columns[colIndex].Name);

			if (collectionColumns is not null && collectionColumns[colIndex] && reader.TokenType != JsonTokenType.StartArray)
			{
				writer.WriteStartArray();
				if (!TryWriteCurrentValue(ref reader, writer))
					return false;
				writer.WriteEndArray();
			}
			else if (!TryWriteCurrentValue(ref reader, writer))
			{
				return false;
			}

			colIndex++;
		}

		if (colIndex < columns.Length)
			throw new JsonException($"ES|QL row contains fewer values ({colIndex}) than declared columns ({columns.Length}).");

		writer.WriteEndObject();
		writer.Flush();
		return true;
	}

	private static bool IsPrimitiveJsonType(Type type)
	{
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t.IsPrimitive || t == typeof(decimal) || t == typeof(string) || t.IsEnum;
	}

	private static bool TryWriteScalarValue(ref Utf8JsonReader reader, ArrayBufferWriter<byte> buffer)
	{
		buffer.ResetWrittenCount();

		if (!reader.Read())
			return false;

		using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });

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

	/// <summary>
	/// Builds a <see cref="ColumnLayout"/> for the target type and the ES|QL columns.
	/// </summary>
	private ColumnLayout GetColumnLayout<T>(ColumnInfo[] columns) =>
		ColumnLayout.Build(columns, typeof(T), _metadata);

	private static bool TryWriteRowAsNestedObject(
		ref Utf8JsonReader reader,
		ReadOnlySpan<ColumnInfo> columns,
		ColumnLayout layout,
		ArrayBufferWriter<byte> rowBuffer,
		ArrayBufferWriter<byte> valueBuffer)
	{
		rowBuffer.ResetWrittenCount();
		valueBuffer.ResetWrittenCount();

		var columnCount = columns.Length;

		ValueSlice[]? rentedSlices = null;
		var slices = columnCount <= 64
			? stackalloc ValueSlice[columnCount]
			: (rentedSlices = ArrayPool<ValueSlice>.Shared.Rent(columnCount)).AsSpan(0, columnCount);

		try
		{
			var writerOptions = new JsonWriterOptions { SkipValidation = true };

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

				using (var valueWriter = new Utf8JsonWriter(valueBuffer, writerOptions))
				{
					if (!TryWriteCurrentValue(ref reader, valueWriter))
						return false;
				}

				var length = valueBuffer.WrittenCount - start;
				slices[colIndex] = new ValueSlice(start, length, firstToken, IsNull: false);
				colIndex++;
			}

			if (colIndex < columnCount)
				throw new JsonException($"ES|QL row contains fewer values ({colIndex}) than declared columns ({columnCount}).");

			using var rowWriter = new Utf8JsonWriter(rowBuffer, writerOptions);
			rowWriter.WriteStartObject();
			WriteChildren(layout.Root.Children!, rowWriter, valueBuffer.WrittenSpan, slices);
			rowWriter.WriteEndObject();
			rowWriter.Flush();

			return true;
		}
		finally
		{
			if (rentedSlices is not null)
				ArrayPool<ValueSlice>.Shared.Return(rentedSlices);
		}
	}

	private static void WriteChildren(
		List<ColumnNode> children,
		Utf8JsonWriter writer,
		ReadOnlySpan<byte> values,
		ReadOnlySpan<ValueSlice> slices)
	{
		foreach (var child in children)
		{
			if (child.ColumnIndex >= 0)
			{
				var slice = slices[child.ColumnIndex];
				if (slice.IsNull)
					continue;

				writer.WritePropertyName(child.PropertyName);
				var raw = values.Slice(slice.Start, slice.Length);

				if (child.IsCollection && slice.FirstToken != JsonTokenType.StartArray)
				{
					writer.WriteStartArray();
					writer.WriteRawValue(raw, skipInputValidation: true);
					writer.WriteEndArray();
				}
				else
				{
					writer.WriteRawValue(raw, skipInputValidation: true);
				}
			}
			else
			{
				if (child.Children is null || ColumnLayout.AllChildrenNull(child, slices))
					continue;

				writer.WritePropertyName(child.PropertyName);
				writer.WriteStartObject();
				WriteChildren(child.Children, writer, values, slices);
				writer.WriteEndObject();
			}
		}
	}
}
