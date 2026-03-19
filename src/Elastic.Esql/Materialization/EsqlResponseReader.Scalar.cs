// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	/// <summary>
	/// Holds the result of a scalar ES|QL query together with the total row count for cardinality validation.
	/// </summary>
	public readonly record struct ScalarResult<T>(T? Value, int RowCount);

	/// <summary>
	/// Reads the first row of an ES|QL response from a <see cref="Stream"/> as an instance of <typeparamref name="T"/> and counts total rows for
	/// cardinality validation .
	/// </summary>
	public async Task<ScalarResult<T>> ReadScalarAsync<T>(
		Stream stream,
		CancellationToken cancellationToken = default)
	{
		using var asyncBuffer = new AsyncStreamBuffer(stream);
		var cursor = new AsyncStreamBufferCursor(asyncBuffer);
		return await ReadScalarAsync<T>(cursor, cancellationToken).ConfigureAwait(false);
	}

#if NET10_0_OR_GREATER
	/// <summary>
	/// Reads the first row of an ES|QL response from a <see cref="PipeReader"/> as an instance of <typeparamref name="T"/> and counts total rows for
	/// cardinality validation.
	/// </summary>
	public async Task<ScalarResult<T>> ReadScalarAsync<T>(
		PipeReader pipeReader,
		CancellationToken cancellationToken = default)
	{
		var cursor = new PipeReaderCursor(pipeReader);
		return await ReadScalarAsync<T>(cursor, cancellationToken).ConfigureAwait(false);
	}
#endif

	/// <summary>
	/// Reads the first row of an ES|QL response from a <see cref="Stream"/> as an instance of <typeparamref name="T"/> and counts total rows for
	/// cardinality validation.
	/// </summary>
	public ScalarResult<T> ReadScalar<T>(Stream stream)
	{
		using var syncBuffer = new SyncStreamBuffer(stream);
		var cursor = new SyncStreamBufferCursor(syncBuffer);
		return ReadScalar<T>(cursor);
	}

	private async Task<ScalarResult<T>> ReadScalarAsync<T>(
		IAsyncBufferCursor cursor,
		CancellationToken cancellationToken)
	{
		var prepared = await PrepareRowsAsync<T>(cursor, cancellationToken).ConfigureAwait(false);
		var (columns, readerState, layout) = (prepared.Columns, prepared.ReaderState, prepared.Layout);
		var plan = CreateRowMaterializationPlan<T>(columns, Options);

		var rowBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		var valueBuffer = plan.IsScalar ? null : new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		await using var valueWriter = plan.IsScalar ? null : new Utf8JsonWriter(valueBuffer!, SkipValidationWriterOptions);
		await using var scalarWriter = plan.IsScalar ? new Utf8JsonWriter(rowBuffer, SkipValidationWriterOptions) : null;

		T? value = default;
		var rowCount = 0;
		var done = false;

		while (!done)
		{
			if (!await cursor.ReadAsync(cancellationToken).ConfigureAwait(false))
				break;

			var buffer = cursor.Buffer;
			ConsumeScalarRowsChunk(
				ref buffer,
				cursor.IsCompleted,
				ref readerState,
				layout,
				rowBuffer,
				valueBuffer,
				valueWriter,
				scalarWriter,
				plan.TypeInfo,
				Options,
				ref value,
				ref rowCount,
				ref done
			);

			cursor.AdvanceTo(buffer.Start, buffer.End);

			if (cursor.IsCompleted)
				break;
		}

		return new ScalarResult<T>(value, rowCount);
	}

	private ScalarResult<T> ReadScalar<T>(ISyncBufferCursor cursor)
	{
		var prepared = PrepareRows<T>(cursor);
		var (columns, readerState, layout) = (prepared.Columns, prepared.ReaderState, prepared.Layout);
		var plan = CreateRowMaterializationPlan<T>(columns, Options);

		var rowBuffer = new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		var valueBuffer = plan.IsScalar ? null : new ArrayBufferWriter<byte>(plan.EstimatedRowSize);
		using var valueWriter = plan.IsScalar ? null : new Utf8JsonWriter(valueBuffer!, SkipValidationWriterOptions);
		using var scalarWriter = plan.IsScalar ? new Utf8JsonWriter(rowBuffer, SkipValidationWriterOptions) : null;

		T? value = default;
		var rowCount = 0;
		var done = false;

		while (!done)
		{
			if (!cursor.Read() && cursor.IsCompleted && cursor.Buffer.IsEmpty)
				break;

			var buffer = cursor.Buffer;
			ConsumeScalarRowsChunk(
				ref buffer,
				cursor.IsCompleted,
				ref readerState,
				layout,
				rowBuffer,
				valueBuffer,
				valueWriter,
				scalarWriter,
				plan.TypeInfo,
				Options,
				ref value,
				ref rowCount,
				ref done
			);

			cursor.AdvanceTo(buffer.Start, buffer.End);
		}

		return new ScalarResult<T>(value, rowCount);
	}

	private static void ConsumeScalarRowsChunk<T>(
		ref ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState readerState,
		ColumnLayout layout,
		ArrayBufferWriter<byte> rowBuffer,
		ArrayBufferWriter<byte>? valueBuffer,
		Utf8JsonWriter? valueWriter,
		Utf8JsonWriter? scalarWriter,
		JsonTypeInfo<T>? typeInfo,
		JsonSerializerOptions options,
		ref T? value,
		ref int rowCount,
		ref bool done)
	{
		while (true)
		{
			if (rowCount == 0)
			{
				if (!TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter, typeInfo, options, out var item, out var reachedEnd))
					return;

				if (reachedEnd)
				{
					done = true;
					return;
				}

				value = item;
				rowCount = 1;
				continue;
			}

			if (!TrySkipRow(ref buffer, isFinalBlock, ref readerState, out var reachedEndSkip))
				return;

			if (reachedEndSkip)
			{
				done = true;
				return;
			}

			rowCount++;
		}
	}

	private static bool TrySkipRow(
		ref ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
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

		if (!reader.TrySkip())
		{
			state = savedState;
			buffer = savedBuffer;
			return false;
		}

		state = reader.CurrentState;
		buffer = buffer.Slice(reader.Position);
		return true;
	}
}
