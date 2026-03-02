// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Elastic.Esql.Materialization;

/// <summary>Holds the result of a scalar ES|QL query together with the total row count for cardinality validation.</summary>
public readonly record struct ScalarResult<T>(T? Value, int RowCount);

/// <summary>
/// Streams ES|QL row-oriented (columnar=false) JSON responses into <c>T</c> instances with minimal allocations.
/// </summary>
public static class EsqlResponseReader
{
	/// <summary>
	/// Reads the first row of an ES|QL response as an instance of <typeparamref name="T"/>
	/// and counts total rows for cardinality validation (First/Single semantics).
	/// </summary>
	public static async Task<ScalarResult<T>> ReadScalarAsync<T>(
		Stream stream,
		JsonSerializerOptions options,
		CancellationToken cancellationToken = default)
	{
		var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(
			pool: MemoryPool<byte>.Shared,
			bufferSize: 4096,
			leaveOpen: true));

		try
		{
			return await ReadScalarAsync<T>(pipeReader, options, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Reads the first row of an ES|QL response from a <see cref="PipeReader"/> as an instance of
	/// <typeparamref name="T"/> and counts total rows for cardinality validation.
	/// </summary>
	public static async Task<ScalarResult<T>> ReadScalarAsync<T>(
		PipeReader pipeReader,
		JsonSerializerOptions options,
		CancellationToken cancellationToken = default)
	{
		var (columns, readerState, consumed, examined) =
			await ReadColumnsFromPipeAsync(pipeReader, cancellationToken)
				.ConfigureAwait(false);

		pipeReader.AdvanceTo(consumed, examined);

		readerState = await AdvanceToValuesArrayFromPipeAsync(
			pipeReader, readerState, cancellationToken).ConfigureAwait(false);

		var rowBuffer = new ArrayBufferWriter<byte>(256);
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
					if (!TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, columns, rowBuffer, options, out var item, out var reachedEnd))
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
	/// Materializes each row of an ES|QL response stream as an instance of
	/// <typeparamref name="T"/>.
	/// </summary>
	public static async IAsyncEnumerable<T> ReadRowsAsync<T>(
		Stream stream,
		JsonSerializerOptions options,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(
			pool: MemoryPool<byte>.Shared,
			bufferSize: 4096,
			leaveOpen: true));

		try
		{
			await foreach (var item in ReadRowsAsync<T>(pipeReader, options, cancellationToken)
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
	/// This overload avoids any intermediate buffering beyond what the pipe itself provides and is the preferred entry-point when you already own
	/// a <see cref="PipeReader"/>.
	/// </summary>
	public static async IAsyncEnumerable<T> ReadRowsAsync<T>(
		PipeReader pipeReader,
		JsonSerializerOptions options,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// 1. Parse "columns" incrementally from the pipe.
		var (columns, readerState, consumed, examined) =
			await ReadColumnsFromPipeAsync(pipeReader, cancellationToken)
				.ConfigureAwait(false);

		pipeReader.AdvanceTo(consumed, examined);

		// 2. Advance past everything until we're inside "values".
		readerState = await AdvanceToValuesArrayFromPipeAsync(
			pipeReader, readerState, cancellationToken).ConfigureAwait(false);

		// 3. Stream rows one at a time.
		var rowBuffer = new ArrayBufferWriter<byte>(256);
		var done = false;

		while (!done)
		{
			var result = await pipeReader.ReadAsync(cancellationToken)
				.ConfigureAwait(false);

			var buffer = result.Buffer;
			var isFinalBlock = result.IsCompleted;
			var reachedEnd = false;

			while (TryReadNextRow<T>(ref buffer, isFinalBlock, ref readerState, columns, rowBuffer, options, out var item, out reachedEnd))
			{
				yield return item!;

				if (reachedEnd)
				{
					done = true;
					break;
				}
			}

			if (reachedEnd)
				done = true;

			pipeReader.AdvanceTo(buffer.Start, buffer.End);

			if (result.IsCompleted)
				break;
		}
	}

	private readonly record struct ColumnInfo(string Name, string Type);

	private static async Task<(ColumnInfo[] Columns, JsonReaderState State, SequencePosition Consumed, SequencePosition Examined)>
		ReadColumnsFromPipeAsync(
			PipeReader pipeReader,
			CancellationToken ct)
	{
		var state = new JsonReaderState();

		while (true)
		{
			var result = await pipeReader.ReadAsync(ct).ConfigureAwait(false);
			var buffer = result.Buffer;

			if (TryParseColumns(buffer, result.IsCompleted, ref state, out var columns, out var consumed))
				return (columns!, state, consumed, buffer.End);

			pipeReader.AdvanceTo(buffer.Start, buffer.End);

			if (result.IsCompleted)
				throw new JsonException("Stream ended before \"columns\" array was fully read.");
		}
	}

	private static bool TryParseColumns(
		ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		out ColumnInfo[]? columns,
		out SequencePosition consumed)
	{
		columns = null;
		var reader = new Utf8JsonReader(buffer, isFinalBlock, state);

		var foundColumns = false;
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.PropertyName &&
				reader.ValueTextEquals("columns"u8))
			{
				foundColumns = true;
				break;
			}
		}

		if (!foundColumns)
		{
			state = reader.CurrentState;
			consumed = reader.Position;
			return false;
		}

		if (!reader.Read()) // StartArray
		{
			state = reader.CurrentState;
			consumed = reader.Position;
			return false;
		}

		var list = new List<ColumnInfo>(16);

		while (true)
		{
			if (!reader.Read())
			{
				state = reader.CurrentState;
				consumed = reader.Position;
				return false;
			}

			if (reader.TokenType == JsonTokenType.EndArray)
				break;

			if (reader.TokenType != JsonTokenType.StartObject)
				continue;

			string? name = null;
			string? type = null;

			while (true)
			{
				if (!reader.Read())
				{
					state = reader.CurrentState;
					consumed = reader.Position;
					return false;
				}

				if (reader.TokenType == JsonTokenType.EndObject)
					break;

				if (reader.TokenType != JsonTokenType.PropertyName)
					continue;

				if (reader.ValueTextEquals("name"u8))
				{
					if (!reader.Read())
					{
						state = reader.CurrentState;
						consumed = reader.Position;
						return false;
					}
					name = reader.GetString();
				}
				else if (reader.ValueTextEquals("type"u8))
				{
					if (!reader.Read())
					{
						state = reader.CurrentState;
						consumed = reader.Position;
						return false;
					}
					type = reader.GetString();
				}
				else
				{
					if (reader.TrySkip())
						continue;

					state = reader.CurrentState;
					consumed = reader.Position;
					return false;
				}
			}

			list.Add(new ColumnInfo(
				name ?? throw new JsonException("ES|QL column is missing the \"name\" property."),
				type ?? throw new JsonException("ES|QL column is missing the \"type\" property.")));
		}

		columns = [.. list];
		state = reader.CurrentState;
		consumed = reader.Position;
		return true;
	}

	private static async Task<JsonReaderState> AdvanceToValuesArrayFromPipeAsync(
		PipeReader pipeReader,
		JsonReaderState state,
		CancellationToken ct)
	{
		while (true)
		{
			var result = await pipeReader.ReadAsync(ct).ConfigureAwait(false);
			var buffer = result.Buffer;

			var reader = new Utf8JsonReader(buffer, result.IsCompleted, state);

			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.PropertyName &&
					reader.ValueTextEquals("values"u8))
				{
					if (!reader.Read()) // StartArray
						break;

					state = reader.CurrentState;
					pipeReader.AdvanceTo(reader.Position, buffer.End);
					return state;
				}
			}

			state = reader.CurrentState;
			pipeReader.AdvanceTo(reader.Position, buffer.End);

			if (result.IsCompleted)
				throw new JsonException("ES|QL response does not contain a \"values\" property.");
		}
	}

	/// <summary>
	/// Attempts to read a single complete row from <paramref name="buffer"/>.
	/// On success the buffer is sliced past the consumed bytes.
	/// </summary>
	private static bool TryReadNextRow<T>(
		ref ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		ReadOnlySpan<ColumnInfo> columns,
		ArrayBufferWriter<byte> rowBuffer,
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

		if (!TryWriteRowAsObject(ref reader, columns, rowBuffer))
		{
			// Incomplete row. Restore caller's state so the partial bytes are re-read after more data arrives from the pipe.
			state = savedState;
			buffer = savedBuffer;
			return false;
		}

		item = JsonSerializer.Deserialize<T>(rowBuffer.WrittenSpan, options);

		state = reader.CurrentState;
		buffer = buffer.Slice(reader.Position);
		return true;
	}

	/// <summary>
	/// Skips a single row (array) without materializing it, used for counting remaining rows.
	/// </summary>
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

		// Skip past the entire row array
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

	/// <summary>
	/// Rewrites the current row array as a named JSON object into <paramref name="buffer"/>.
	/// Returns <see langword="false"/> if the reader ran out of data before the row was complete.
	/// </summary>
	private static bool TryWriteRowAsObject(
		ref Utf8JsonReader reader,
		ReadOnlySpan<ColumnInfo> columns,
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

			writer.WritePropertyName(columns[colIndex].Name);

			if (!TryWriteCurrentValue(ref reader, writer))
				return false;

			colIndex++;
		}

		writer.WriteEndObject();
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

	/// <summary>
	/// Deep-copies a nested array or object from reader → writer.
	/// </summary>
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
