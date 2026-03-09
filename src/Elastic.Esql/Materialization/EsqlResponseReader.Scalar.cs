// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

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
		var pipeReader = CreatePipeReader(stream);

		try
		{
			return await ReadScalarAsync<T>(pipeReader, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Reads the first row of an ES|QL response from a <see cref="PipeReader"/> as an instance of <typeparamref name="T"/> and counts total rows for
	/// cardinality validation.
	/// </summary>
	public async Task<ScalarResult<T>> ReadScalarAsync<T>(
		PipeReader pipeReader,
		CancellationToken cancellationToken = default)
	{
		var (columns, readerState, consumed, examined, _, _) =
			await ReadColumnsFromPipeAsync(pipeReader, cancellationToken)
				.ConfigureAwait(false);

		pipeReader.AdvanceTo(consumed, examined);

		(readerState, _, _) = await AdvanceToValuesArrayFromPipeAsync(
			pipeReader, readerState, cancellationToken).ConfigureAwait(false);

		var layout = GetColumnLayout<T>(columns);
		var estimatedRowSize = Math.Max(256, columns.Length * 32);

		var rowBuffer = new ArrayBufferWriter<byte>(estimatedRowSize);
		var valueBuffer = new ArrayBufferWriter<byte>(estimatedRowSize);
		using var valueWriter = new Utf8JsonWriter(valueBuffer, SkipValidationWriterOptions);

		var isScalar = columns.Length == 1 && IsPrimitiveJsonType(typeof(T));
		using var scalarWriter = isScalar ? new Utf8JsonWriter(rowBuffer, SkipValidationWriterOptions) : null;

		T? value = default;
		var rowCount = 0;
		var done = false;

		while (!done)
		{
			var result = await pipeReader.ReadAsync(cancellationToken)
				.ConfigureAwait(false);

			var buffer = result.Buffer;
			var isFinalBlock = result.IsCompleted;

			while (true)
			{
				if (rowCount == 0)
				{
					if (!TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter, Options, out var item, out var reachedEnd))
						break;

					if (reachedEnd)
					{
						done = true;
						break;
					}

					value = item;
					rowCount = 1;
				}
				else
				{
					if (!TrySkipRow(ref buffer, isFinalBlock, ref readerState, out var reachedEnd))
						break;

					if (reachedEnd)
					{
						done = true;
						break;
					}

					rowCount++;
				}
			}

			pipeReader.AdvanceTo(buffer.Start, buffer.End);

			if (result.IsCompleted)
				break;
		}

		return new ScalarResult<T>(value, rowCount);
	}

	/// <summary>
	/// Reads the first row of an ES|QL response from a <see cref="Stream"/> as an instance of <typeparamref name="T"/> and counts total rows for
	/// cardinality validation.
	/// </summary>
	public ScalarResult<T> ReadScalar<T>(Stream stream)
	{
		using var syncBuffer = new SyncStreamBuffer(stream);

		var (columns, readerState, consumed, _, _) = ReadColumnsFromStream(syncBuffer);
		syncBuffer.AdvanceTo(consumed);

		(readerState, _, _) = AdvanceToValuesArrayFromStream(syncBuffer, readerState);

		var layout = GetColumnLayout<T>(columns);
		var estimatedRowSize = Math.Max(256, columns.Length * 32);

		var rowBuffer = new ArrayBufferWriter<byte>(estimatedRowSize);
		var valueBuffer = new ArrayBufferWriter<byte>(estimatedRowSize);
		using var valueWriter = new Utf8JsonWriter(valueBuffer, SkipValidationWriterOptions);

		var isScalar = columns.Length == 1 && IsPrimitiveJsonType(typeof(T));
		using var scalarWriter = isScalar ? new Utf8JsonWriter(rowBuffer, SkipValidationWriterOptions) : null;

		T? value = default;
		var rowCount = 0;
		var done = false;

		while (!done)
		{
			if (!syncBuffer.Read() && syncBuffer.IsCompleted && syncBuffer.Buffer.IsEmpty)
				break;

			var buffer = syncBuffer.Buffer;
			var isFinalBlock = syncBuffer.IsCompleted;

			while (true)
			{
				if (rowCount == 0)
				{
					if (!TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, layout, rowBuffer, valueBuffer, valueWriter, scalarWriter, Options, out var item, out var reachedEnd))
						break;

					if (reachedEnd)
					{
						done = true;
						break;
					}

					value = item;
					rowCount = 1;
				}
				else
				{
					if (!TrySkipRow(ref buffer, isFinalBlock, ref readerState, out var reachedEnd))
						break;

					if (reachedEnd)
					{
						done = true;
						break;
					}

					rowCount++;
				}
			}

			syncBuffer.AdvanceTo(buffer.Start, buffer.End);
		}

		return new ScalarResult<T>(value, rowCount);
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
