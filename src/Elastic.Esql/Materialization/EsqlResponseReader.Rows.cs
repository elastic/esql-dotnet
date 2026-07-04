// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	private sealed class ReaderStateTracker(JsonReaderState state)
	{
		public JsonReaderState State { get; set; } = state;

		public void Set(JsonReaderState state) => State = state;
	}

	/// <summary>Reads rows from an ES|QL response stream. Metadata is eagerly parsed before returning.</summary>
	public async Task<EsqlAsyncResults<T>> ReadRowsAsync<T>(
		Stream stream, bool requireId = false, CancellationToken cancellationToken = default)
	{
		var asyncBuffer = new AsyncStreamBuffer(stream);

		try
		{
			var cursor = new AsyncStreamBufferCursor(asyncBuffer);
			var prepared = await PrepareRowsAsync<T>(cursor, cancellationToken).ConfigureAwait(false);

			var result = new EsqlAsyncResults<T>();
			result.SetOwnedResource(asyncBuffer);
			await ApplyPreparedMetadataAsync(result, prepared, cursor, cancellationToken).ConfigureAwait(false);

			var forceBuffer = requireId && result.Id is null && !prepared.ValuesFirst && prepared.IsRunning != true;
			result.Rows = forceBuffer
				? StreamRowsThenScanForIdAsync(cursor, prepared, result, cancellationToken)
				: BuildAsyncRows(cursor, prepared, result, cancellationToken);
			return result;
		}
		catch
		{
			// Ownership only transfers to the caller on successful return; reclaim the rented buffer on failure.
			asyncBuffer.Dispose();
			throw;
		}
	}

#if NET10_0_OR_GREATER
	/// <summary>Reads rows from an ES|QL response pipe. Metadata is eagerly parsed before returning.</summary>
	public async Task<EsqlAsyncResults<T>> ReadRowsAsync<T>(
		PipeReader pipeReader, bool requireId = false, CancellationToken cancellationToken = default)
	{
		var cursor = new PipeReaderCursor(pipeReader);
		var prepared = await PrepareRowsAsync<T>(cursor, cancellationToken).ConfigureAwait(false);

		var result = new EsqlAsyncResults<T>();
		await ApplyPreparedMetadataAsync(result, prepared, cursor, cancellationToken).ConfigureAwait(false);

		var forceBuffer = requireId && result.Id is null && !prepared.ValuesFirst && prepared.IsRunning != true;
		result.Rows = forceBuffer
			? StreamRowsThenScanForIdWithPipeCleanupAsync(cursor, pipeReader, prepared, result, cancellationToken)
			: BuildAsyncRowsWithPipeCleanup(cursor, pipeReader, prepared, result, cancellationToken);
		return result;
	}
#endif

	/// <summary>Reads rows from an ES|QL response stream synchronously. Metadata is eagerly parsed before returning.</summary>
	public EsqlResults<T> ReadRows<T>(Stream stream, bool requireId = false)
	{
		var syncBuffer = new SyncStreamBuffer(stream);

		try
		{
			var cursor = new SyncStreamBufferCursor(syncBuffer);
			var prepared = PrepareRows<T>(cursor);

			var result = new EsqlResults<T>();
			result.SetOwnedResource(syncBuffer);
			ApplyPreparedMetadata(result, prepared, cursor);

			var forceBuffer = requireId && result.Id is null && !prepared.ValuesFirst && prepared.IsRunning != true;
			result.Rows = forceBuffer
				? StreamRowsThenScanForId(cursor, prepared, result)
				: BuildSyncRows(cursor, prepared, result);
			return result;
		}
		catch
		{
			// Ownership only transfers to the caller on successful return; reclaim the rented buffer on failure.
			syncBuffer.Dispose();
			throw;
		}
	}

	private static async Task ApplyPreparedMetadataAsync<T>(
		EsqlAsyncResults<T> result, PrepareRowsResult prepared, IAsyncBufferCursor cursor, CancellationToken ct)
	{
		result.Id = prepared.Id;
		result.IsRunning = prepared.IsRunning;

		if (prepared.IsRunning is null && prepared.Columns.Length > 0)
			result.IsRunning = false;

		if (prepared.IsRunning == true && prepared.Id is null)
		{
			var (id, _) = await ScanForIdAsync(cursor, prepared.ReaderState, ct).ConfigureAwait(false);
			result.Id = id;
		}
	}

	private static void ApplyPreparedMetadata<T>(
		EsqlResults<T> result, PrepareRowsResult prepared, ISyncBufferCursor cursor)
	{
		result.Id = prepared.Id;
		result.IsRunning = prepared.IsRunning;

		if (prepared.IsRunning is null && prepared.Columns.Length > 0)
			result.IsRunning = false;

		if (prepared.IsRunning == true && prepared.Id is null)
		{
			var (id, _) = ScanForId(cursor, prepared.ReaderState);
			result.Id = id;
		}
	}

	private async IAsyncEnumerable<T> BuildAsyncRows<T>(
		IAsyncBufferCursor cursor,
		PrepareRowsResult prepared,
		EsqlAsyncResults<T> result,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (prepared.IsRunning == true)
			yield break;

		if (prepared.ValuesFirst)
		{
			await foreach (var item in ReadFromBufferedResponseAsync<T>(cursor, result, cancellationToken).ConfigureAwait(false))
				yield return item;
			yield break;
		}

		await foreach (var item in StreamRowsAsync<T>(cursor, prepared.ReaderState, prepared.Columns, prepared.Layout, Options, cancellationToken: cancellationToken)
			.ConfigureAwait(false))
			yield return item;
	}

#if NET10_0_OR_GREATER
	private async IAsyncEnumerable<T> BuildAsyncRowsWithPipeCleanup<T>(
		PipeReaderCursor cursor,
		PipeReader pipeReader,
		PrepareRowsResult prepared,
		EsqlAsyncResults<T> result,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var item in BuildAsyncRows(cursor, prepared, result, cancellationToken).ConfigureAwait(false))
				yield return item;
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}
	}
#endif

	private IEnumerable<T> BuildSyncRows<T>(
		ISyncBufferCursor cursor,
		PrepareRowsResult prepared,
		EsqlResults<T> result)
	{
		if (prepared.IsRunning == true)
			yield break;

		if (prepared.ValuesFirst)
		{
			foreach (var item in ReadFromBufferedResponse<T>(cursor, result))
				yield return item;
			yield break;
		}

		foreach (var item in StreamRows<T>(cursor, prepared.ReaderState, prepared.Columns, prepared.Layout, Options))
			yield return item;
	}

	private async IAsyncEnumerable<T> StreamRowsThenScanForIdAsync<T>(
		IAsyncBufferCursor cursor,
		PrepareRowsResult prepared,
		EsqlAsyncResults<T> result,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var tracker = new ReaderStateTracker(prepared.ReaderState);

		await foreach (var item in StreamRowsAsync<T>(cursor, prepared.ReaderState, prepared.Columns, prepared.Layout, Options, cancellationToken, tracker).ConfigureAwait(false))
			yield return item;

		var (id, _) = await ScanForIdAsync(cursor, tracker.State, cancellationToken).ConfigureAwait(false);
		result.Id ??= id;
	}

#if NET10_0_OR_GREATER
	private async IAsyncEnumerable<T> StreamRowsThenScanForIdWithPipeCleanupAsync<T>(
		PipeReaderCursor cursor,
		PipeReader pipeReader,
		PrepareRowsResult prepared,
		EsqlAsyncResults<T> result,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var item in StreamRowsThenScanForIdAsync(cursor, prepared, result, cancellationToken).ConfigureAwait(false))
				yield return item;
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}
	}
#endif

	private IEnumerable<T> StreamRowsThenScanForId<T>(
		ISyncBufferCursor cursor,
		PrepareRowsResult prepared,
		EsqlResults<T> result)
	{
		var tracker = new ReaderStateTracker(prepared.ReaderState);

		foreach (var item in StreamRows<T>(cursor, prepared.ReaderState, prepared.Columns, prepared.Layout, Options, tracker))
			yield return item;

		var (id, _) = ScanForId(cursor, tracker.State);
		result.Id ??= id;
	}

	private readonly record struct BufferedStreamResult<T>(IEnumerable<T> Rows, string? Id, bool? IsRunning);

	private BufferedStreamResult<T> StreamFromBuffer<T>(byte[] buffer, int length)
	{
		var (columns, valuesOffset, id, isRunning) = ParseColumnsFromBuffer(buffer, length);
		var layout = GetColumnLayout<T>(columns);
		return new BufferedStreamResult<T>(StreamRowsFromBuffer<T>(buffer, length, valuesOffset, columns, layout), id, isRunning);
	}

	private IEnumerable<T> StreamRowsFromBuffer<T>(byte[] buffer, int length, int valuesOffset, ColumnInfo[] columns, ColumnLayout layout)
	{
		var bufferCursor = new DrainedBufferCursor(buffer, valuesOffset, length);

		var readerState = new JsonReaderState();
		if (!AdvancePastStartArray(bufferCursor, ref readerState))
			yield break;

		foreach (var item in StreamRows<T>(bufferCursor, readerState, columns, layout, Options))
			yield return item;
	}

	private async IAsyncEnumerable<T> ReadFromBufferedResponseAsync<T>(
		IAsyncBufferCursor cursor,
		EsqlAsyncResults<T> result,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var drained = await DrainToBufferAsync(cursor, cancellationToken).ConfigureAwait(false);
		result.SetBuffer(drained.Buffer, drained.IsRented);

		var parsed = StreamFromBuffer<T>(drained.Buffer, drained.Length);
		result.Id ??= parsed.Id;
		result.IsRunning ??= parsed.IsRunning;

		foreach (var item in parsed.Rows)
			yield return item;

		result.ReleaseBuffer();
	}

	private IEnumerable<T> ReadFromBufferedResponse<T>(
		ISyncBufferCursor cursor,
		EsqlResults<T> result)
	{
		var drained = DrainToBuffer(cursor);
		result.SetBuffer(drained.Buffer, drained.IsRented);

		var parsed = StreamFromBuffer<T>(drained.Buffer, drained.Length);
		result.Id ??= parsed.Id;
		result.IsRunning ??= parsed.IsRunning;

		foreach (var item in parsed.Rows)
			yield return item;

		result.ReleaseBuffer();
	}

	/// <summary>Streams rows from the <c>values</c> array, one at a time for flat layouts and in batches for nested layouts.</summary>
	private static async IAsyncEnumerable<T> StreamRowsAsync<T>(
		IAsyncBufferCursor cursor,
		JsonReaderState readerState,
		ColumnInfo[] columns,
		ColumnLayout layout,
		JsonSerializerOptions options,
		[EnumeratorCancellation] CancellationToken cancellationToken,
		ReaderStateTracker? readerStateTracker = null)
	{
		var plan = CreateRowMaterializationPlan<T>(columns, options);

		// Batching only pays off for nested layouts, where every per-row Deserialize call
		// allocates serializer-internal depth-tracking state. Flat layouts keep true
		// row-at-a-time streaming.
		if (layout.BranchNodeCount > 0 && TryResolveListTypeInfo<T>(options) is { } listTypeInfo)
		{
			await foreach (var item in StreamRowsBatchedAsync(cursor, readerState, plan, layout, listTypeInfo, cancellationToken, readerStateTracker).ConfigureAwait(false))
				yield return item;
			yield break;
		}

		var rowBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		var valueBuffer = plan.IsScalar ? null : new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		await using var valueWriter = plan.IsScalar ? null : new Utf8JsonWriter(valueBuffer!, SkipValidationWriterOptions);
		await using var scalarWriter = plan.IsScalar ? new Utf8JsonWriter(rowBuffer, SkipValidationWriterOptions) : null;

		try
		{
			var done = false;

			while (!done)
			{
				if (!await cursor.ReadAsync(cancellationToken).ConfigureAwait(false))
					break;

				var buffer = cursor.Buffer;
				var isFinalBlock = cursor.IsEofReached;
				var reachedEnd = false;

				while (TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter, plan.TypeInfo, options, out var item, out reachedEnd))
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

				cursor.AdvanceTo(buffer.Start, buffer.End);

				if (cursor.IsEofReached)
					break;
			}
		}
		finally
		{
			readerStateTracker?.Set(readerState);
		}
	}

	private static IEnumerable<T> StreamRows<T>(
		ISyncBufferCursor cursor,
		JsonReaderState readerState,
		ColumnInfo[] columns,
		ColumnLayout layout,
		JsonSerializerOptions options,
		ReaderStateTracker? readerStateTracker = null)
	{
		var plan = CreateRowMaterializationPlan<T>(columns, options);

		// Batching only pays off for nested layouts, where every per-row Deserialize call
		// allocates serializer-internal depth-tracking state. Flat layouts keep true
		// row-at-a-time streaming.
		if (layout.BranchNodeCount > 0 && TryResolveListTypeInfo<T>(options) is { } listTypeInfo)
		{
			foreach (var item in StreamRowsBatched(cursor, readerState, plan, layout, listTypeInfo, readerStateTracker))
				yield return item;
			yield break;
		}

		var rowBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		var valueBuffer = plan.IsScalar ? null : new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		using var valueWriter = plan.IsScalar ? null : new Utf8JsonWriter(valueBuffer!, SkipValidationWriterOptions);
		using var scalarWriter = plan.IsScalar ? new Utf8JsonWriter(rowBuffer, SkipValidationWriterOptions) : null;

		try
		{
			var done = false;

			while (!done)
			{
				if (!cursor.Read() && cursor.IsCompleted && cursor.Buffer.IsEmpty)
					break;

				var buffer = cursor.Buffer;
				var isFinalBlock = cursor.IsEofReached;
				var reachedEnd = false;

				while (TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter, plan.TypeInfo, options, out var item, out reachedEnd))
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

				cursor.AdvanceTo(buffer.Start, buffer.End);

				if (cursor.IsEofReached)
					break;
			}
		}
		finally
		{
			readerStateTracker?.Set(readerState);
		}
	}

	// Batch thresholds for nested layouts: every JsonSerializer.Deserialize call on a type with
	// nested objects allocates roughly 0.5 KB of serializer-internal depth-tracking state, so rows
	// are grouped into a single call per batch. 64 rows amortizes that cost to a few bytes per row;
	// the 64 KB cap bounds buffering (and first-item latency) when individual rows are large and
	// keeps the batch buffer below the large-object-heap threshold.
	private const int MaxBatchRowCount = 64;
	private const int MaxBatchBufferBytes = 64 * 1024;

	/// <summary>
	/// Streams rows for nested layouts by assembling up to <see cref="MaxBatchRowCount"/> rows (or
	/// <see cref="MaxBatchBufferBytes"/> bytes) into a JSON array and deserializing each batch with a
	/// single serializer call. Rows are yielded in order; at most one batch is buffered before yielding.
	/// </summary>
	private static async IAsyncEnumerable<T> StreamRowsBatchedAsync<T>(
		IAsyncBufferCursor cursor,
		JsonReaderState readerState,
		RowMaterializationPlan<T> plan,
		ColumnLayout layout,
		JsonTypeInfo<List<T>> listTypeInfo,
		[EnumeratorCancellation] CancellationToken cancellationToken,
		ReaderStateTracker? readerStateTracker = null)
	{
		var rowBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		var valueBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		var batchBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize * 8);
		await using var valueWriter = new Utf8JsonWriter(valueBuffer, SkipValidationWriterOptions);
		var batchRowCount = 0;

		try
		{
			var done = false;

			while (!done)
			{
				if (!await cursor.ReadAsync(cancellationToken).ConfigureAwait(false))
					break;

				var buffer = cursor.Buffer;
				var isFinalBlock = cursor.IsEofReached;
				var reachedEnd = false;

				while (TryAssembleNextRow(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter: null, out reachedEnd))
				{
					if (reachedEnd)
					{
						done = true;
						break;
					}

					AppendRowToBatch(batchBuffer, rowBuffer, batchRowCount);
					batchRowCount++;

					if (batchRowCount < MaxBatchRowCount && batchBuffer.WrittenCount < MaxBatchBufferBytes)
						continue;

					foreach (var item in DeserializeBatch(batchBuffer, listTypeInfo))
						yield return item;

					batchRowCount = 0;
				}

				if (reachedEnd)
					done = true;

				cursor.AdvanceTo(buffer.Start, buffer.End);

				if (cursor.IsEofReached)
					break;
			}

			if (batchRowCount > 0)
			{
				foreach (var item in DeserializeBatch(batchBuffer, listTypeInfo))
					yield return item;
			}
		}
		finally
		{
			readerStateTracker?.Set(readerState);
		}
	}

	/// <summary>
	/// Streams rows for nested layouts by assembling up to <see cref="MaxBatchRowCount"/> rows (or
	/// <see cref="MaxBatchBufferBytes"/> bytes) into a JSON array and deserializing each batch with a
	/// single serializer call. Rows are yielded in order; at most one batch is buffered before yielding.
	/// </summary>
	private static IEnumerable<T> StreamRowsBatched<T>(
		ISyncBufferCursor cursor,
		JsonReaderState readerState,
		RowMaterializationPlan<T> plan,
		ColumnLayout layout,
		JsonTypeInfo<List<T>> listTypeInfo,
		ReaderStateTracker? readerStateTracker)
	{
		var rowBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		var valueBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		var batchBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize * 8);
		using var valueWriter = new Utf8JsonWriter(valueBuffer, SkipValidationWriterOptions);
		var batchRowCount = 0;

		try
		{
			var done = false;

			while (!done)
			{
				if (!cursor.Read() && cursor.IsCompleted && cursor.Buffer.IsEmpty)
					break;

				var buffer = cursor.Buffer;
				var isFinalBlock = cursor.IsEofReached;
				var reachedEnd = false;

				while (TryAssembleNextRow(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter: null, out reachedEnd))
				{
					if (reachedEnd)
					{
						done = true;
						break;
					}

					AppendRowToBatch(batchBuffer, rowBuffer, batchRowCount);
					batchRowCount++;

					if (batchRowCount < MaxBatchRowCount && batchBuffer.WrittenCount < MaxBatchBufferBytes)
						continue;

					foreach (var item in DeserializeBatch(batchBuffer, listTypeInfo))
						yield return item;

					batchRowCount = 0;
				}

				if (reachedEnd)
					done = true;

				cursor.AdvanceTo(buffer.Start, buffer.End);

				if (cursor.IsEofReached)
					break;
			}

			if (batchRowCount > 0)
			{
				foreach (var item in DeserializeBatch(batchBuffer, listTypeInfo))
					yield return item;
			}
		}
		finally
		{
			readerStateTracker?.Set(readerState);
		}
	}

	private static void AppendRowToBatch(ArrayBufferWriter<byte> batchBuffer, ArrayBufferWriter<byte> rowBuffer, int batchRowCount)
	{
		WriteRawByte(batchBuffer, batchRowCount == 0 ? (byte)'[' : (byte)',');
		WriteRawBytes(batchBuffer, rowBuffer.WrittenSpan);
	}

	private static List<T> DeserializeBatch<T>(ArrayBufferWriter<byte> batchBuffer, JsonTypeInfo<List<T>> listTypeInfo)
	{
		WriteRawByte(batchBuffer, (byte)']');
		var items = JsonSerializer.Deserialize(batchBuffer.WrittenSpan, listTypeInfo);
		batchBuffer.ResetWrittenCount();
		return items ?? [];
	}
}
