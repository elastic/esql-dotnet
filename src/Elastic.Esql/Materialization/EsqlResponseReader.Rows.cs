// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Runtime.CompilerServices;
using System.Text.Json;

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
		var cursor = new AsyncStreamBufferCursor(asyncBuffer);
		var prepared = await PrepareRowsAsync<T>(cursor, cancellationToken).ConfigureAwait(false);

		var result = new EsqlAsyncResults<T>();
		result.SetOwnedResource(asyncBuffer);
		await ApplyPreparedMetadataAsync(result, prepared, cursor, cancellationToken).ConfigureAwait(false);

		var forceBuffer = requireId && result.Id is null && !prepared.ValuesFirst && prepared.IsRunning != true;
		result.Rows = forceBuffer
			? ReadFromBufferedResponseAsync<T>(cursor, result, cancellationToken)
			: BuildAsyncRows(cursor, prepared, result, cancellationToken);
		return result;
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
			? ReadFromBufferedResponseAsync<T>(cursor, result, cancellationToken)
			: BuildAsyncRowsWithPipeCleanup(cursor, pipeReader, prepared, result, cancellationToken);
		return result;
	}
#endif

	/// <summary>Reads rows from an ES|QL response stream synchronously. Metadata is eagerly parsed before returning.</summary>
	public EsqlResults<T> ReadRows<T>(Stream stream, bool requireId = false)
	{
		var syncBuffer = new SyncStreamBuffer(stream);
		var cursor = new SyncStreamBufferCursor(syncBuffer);
		var prepared = PrepareRows<T>(cursor);

		var result = new EsqlResults<T>();
		result.SetOwnedResource(syncBuffer);
		ApplyPreparedMetadata(result, prepared, cursor);

		var forceBuffer = requireId && result.Id is null && !prepared.ValuesFirst && prepared.IsRunning != true;
		result.Rows = forceBuffer
			? ReadFromBufferedResponse<T>(cursor, result)
			: BuildSyncRows(cursor, prepared, result);
		return result;
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

	private readonly record struct BufferedStreamResult<T>(IEnumerable<T> Rows, string? Id, bool? IsRunning);

	private BufferedStreamResult<T> StreamFromBuffer<T>(byte[] buffer)
	{
		var (columns, valuesOffset, id, isRunning) = ParseColumnsFromBuffer(buffer, buffer.Length);
		var layout = GetColumnLayout<T>(columns);
		return new BufferedStreamResult<T>(StreamRowsFromBuffer<T>(buffer, valuesOffset, columns, layout), id, isRunning);
	}

	private IEnumerable<T> StreamRowsFromBuffer<T>(byte[] buffer, int valuesOffset, ColumnInfo[] columns, ColumnLayout layout)
	{
		using var memoryStream = new MemoryStream(buffer, valuesOffset, buffer.Length - valuesOffset, writable: false);
		using var syncBuf = new SyncStreamBuffer(memoryStream);
		var bufferCursor = new SyncStreamBufferCursor(syncBuf);

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
		var buffer = await DrainToBufferAsync(cursor, cancellationToken).ConfigureAwait(false);
		result.SetBuffer(buffer);

		var parsed = StreamFromBuffer<T>(buffer);
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
		var buffer = DrainToBuffer(cursor);
		result.SetBuffer(buffer);

		var parsed = StreamFromBuffer<T>(buffer);
		result.Id ??= parsed.Id;
		result.IsRunning ??= parsed.IsRunning;

		foreach (var item in parsed.Rows)
			yield return item;

		result.ReleaseBuffer();
	}

	/// <summary>Streams rows one at a time from the <c>values</c> array.</summary>
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
				var isFinalBlock = cursor.IsCompleted;
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

				if (cursor.IsCompleted)
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
				var isFinalBlock = cursor.IsCompleted;
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
			}
		}
		finally
		{
			readerStateTracker?.Set(readerState);
		}
	}
}
