// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Text.Json;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	internal readonly record struct PrepareRowsResult(ColumnInfo[] Columns, JsonReaderState ReaderState, ColumnLayout Layout, string? Id, bool? IsRunning, bool ValuesFirst);

	private async Task<PrepareRowsResult> PrepareRowsAsync<T>(
		IAsyncBufferCursor cursor,
		CancellationToken cancellationToken)
	{
		var (columns, readerState, consumed, id, isRunning, valuesFirst) = await ReadColumnsFromAsyncCursorAsync(cursor, cancellationToken).ConfigureAwait(false);

		if (valuesFirst)
			return new PrepareRowsResult(columns, readerState, default!, id, isRunning, true);

		cursor.AdvanceTo(consumed, cursor.Buffer.End);

		(readerState, var id2, var isRunning2) = await AdvanceToValuesArrayFromAsyncCursorAsync(cursor, readerState, cancellationToken).ConfigureAwait(false);
		return new PrepareRowsResult(columns, readerState, GetColumnLayout<T>(columns), id ?? id2, isRunning ?? isRunning2, false);
	}

	private PrepareRowsResult PrepareRows<T>(ISyncBufferCursor cursor)
	{
		var (columns, readerState, consumed, id, isRunning, valuesFirst) = ReadColumnsFromSyncCursor(cursor);

		if (valuesFirst)
			return new PrepareRowsResult(columns, readerState, default!, id, isRunning, true);

		cursor.AdvanceTo(consumed, cursor.Buffer.End);

		(readerState, var id2, var isRunning2) = AdvanceToValuesArrayFromSyncCursor(cursor, readerState);
		return new PrepareRowsResult(columns, readerState, GetColumnLayout<T>(columns), id ?? id2, isRunning ?? isRunning2, false);
	}

	/// <summary>Drains all remaining data from an async cursor into a byte array.</summary>
	private static async Task<byte[]> DrainToBufferAsync(IAsyncBufferCursor cursor, CancellationToken ct)
	{
		while (!cursor.IsCompleted)
		{
			if (!await cursor.ReadAsync(ct).ConfigureAwait(false))
				break;
			cursor.AdvanceTo(cursor.Buffer.Start, cursor.Buffer.End);
		}

		var buffer = cursor.Buffer;
		var bytes = new byte[buffer.Length];
		buffer.CopyTo(bytes);
		return bytes;
	}

	/// <summary>Drains all remaining data from a sync cursor into a byte array.</summary>
	private static byte[] DrainToBuffer(ISyncBufferCursor cursor)
	{
		while (!cursor.IsCompleted)
		{
			if (!cursor.Read())
				break;
			cursor.AdvanceTo(cursor.Buffer.Start, cursor.Buffer.End);
		}

		var buffer = cursor.Buffer;
		var bytes = new byte[buffer.Length];
		buffer.CopyTo(bytes);
		return bytes;
	}

	/// <summary>Parses columns and locates the values array from a fully buffered response.</summary>
	private static (ColumnInfo[] Columns, int ValuesOffset, string? Id, bool? IsRunning) ParseColumnsFromBuffer(byte[] buffer, int length)
	{
		var state = new JsonReaderState();
		string? id = null;
		bool? isRunning = null;
		ColumnInfo[]? columns = null;
		var valuesOffset = -1;

		var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(buffer, 0, length), isFinalBlock: true, state);

		while (reader.Read())
		{
			if (reader.TokenType != JsonTokenType.PropertyName)
				continue;

			if (reader.ValueTextEquals("columns"u8))
			{
				if (!reader.Read()) // StartArray
					throw new JsonException("Unexpected end of JSON after \"columns\" property.");

				var list = new List<ColumnInfo>(16);
				while (reader.Read())
				{
					if (reader.TokenType == JsonTokenType.EndArray)
						break;

					if (reader.TokenType != JsonTokenType.StartObject)
						continue;

					string? name = null;
					string? type = null;
					while (reader.Read())
					{
						if (reader.TokenType == JsonTokenType.EndObject)
							break;

						if (reader.TokenType != JsonTokenType.PropertyName)
							continue;

						if (reader.ValueTextEquals("name"u8))
						{
							_ = reader.Read();
							name = reader.GetString();
						}
						else if (reader.ValueTextEquals("type"u8))
						{
							_ = reader.Read();
							type = reader.GetString();
						}
						else
						{
							_ = reader.TrySkip();
						}
					}

					list.Add(new ColumnInfo(
						name ?? throw new JsonException("ES|QL column is missing the \"name\" property."),
						type ?? throw new JsonException("ES|QL column is missing the \"type\" property.")));
				}
				columns = [.. list];
			}
			else if (reader.ValueTextEquals("values"u8))
			{
				_ = reader.Read(); // StartArray
				valuesOffset = (int)reader.TokenStartIndex;
			}
			else if (reader.ValueTextEquals("id"u8))
			{
				_ = reader.Read();
				id = reader.GetString();
			}
			else if (reader.ValueTextEquals("is_running"u8))
			{
				_ = reader.Read();
				isRunning = reader.GetBoolean();
			}
			else
			{
				_ = reader.TrySkip();
			}

			if (columns is not null && valuesOffset >= 0)
				break;
		}

		if (columns is null)
			throw new JsonException("ES|QL response does not contain a \"columns\" property.");

		if (valuesOffset < 0)
			throw new JsonException("ES|QL response does not contain a \"values\" property.");

		return (columns, valuesOffset, id, isRunning);
	}

	private static async Task<(ColumnInfo[] Columns, JsonReaderState State, SequencePosition Consumed, string? Id, bool? IsRunning, bool ValuesFirst)> ReadColumnsFromAsyncCursorAsync(
		IAsyncBufferCursor cursor,
		CancellationToken ct)
	{
		var state = new JsonReaderState();
		string? id = null;
		bool? isRunning = null;

		while (await cursor.ReadAsync(ct).ConfigureAwait(false))
		{
			var buffer = cursor.Buffer;

			if (TryParseColumns(buffer, cursor.IsCompleted, ref state, out var columns, out var consumed, ref id, ref isRunning, out var valuesFirst))
			{
				if (valuesFirst)
					return ([], state, consumed, id, isRunning, true);

				return (columns!, state, consumed, id, isRunning, false);
			}

			if (valuesFirst)
				return ([], state, cursor.Buffer.Start, id, isRunning, true);

			cursor.AdvanceTo(buffer.Start, buffer.End);

			if (cursor.IsCompleted)
				break;
		}

		throw new JsonException("Stream ended before \"columns\" array was fully read.");
	}

	private static (ColumnInfo[] Columns, JsonReaderState State, SequencePosition Consumed, string? Id, bool? IsRunning, bool ValuesFirst) ReadColumnsFromSyncCursor(
		ISyncBufferCursor cursor)
	{
		var state = new JsonReaderState();
		string? id = null;
		bool? isRunning = null;

		while (cursor.Read())
		{
			var buffer = cursor.Buffer;

			if (TryParseColumns(buffer, cursor.IsCompleted, ref state, out var columns, out var consumed, ref id, ref isRunning, out var valuesFirst))
			{
				if (valuesFirst)
					return ([], state, consumed, id, isRunning, true);

				return (columns!, state, consumed, id, isRunning, false);
			}

			if (valuesFirst)
				return ([], state, cursor.Buffer.Start, id, isRunning, true);

			cursor.AdvanceTo(buffer.Start, buffer.End);
		}

		throw new JsonException("Stream ended before \"columns\" array was fully read.");
	}

	private static bool TryParseColumns(
		ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		out ColumnInfo[]? columns,
		out SequencePosition consumed,
		ref string? id,
		ref bool? isRunning,
		out bool valuesEncounteredFirst)
	{
		columns = null;
		valuesEncounteredFirst = false;
		var reader = new Utf8JsonReader(buffer, isFinalBlock, state);

		var foundColumns = false;
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.PropertyName)
			{
				if (reader.ValueTextEquals("columns"u8))
				{
					foundColumns = true;
					break;
				}

				if (reader.ValueTextEquals("values"u8))
				{
					valuesEncounteredFirst = true;
					state = reader.CurrentState;
					consumed = reader.Position;
					return false;
				}

				if (reader.ValueTextEquals("id"u8))
				{
					if (!reader.Read())
					{
						state = reader.CurrentState;
						consumed = reader.Position;
						return false;
					}
					id = reader.GetString();
					continue;
				}

				if (reader.ValueTextEquals("is_running"u8))
				{
					if (!reader.Read())
					{
						state = reader.CurrentState;
						consumed = reader.Position;
						return false;
					}
					isRunning = reader.GetBoolean();
					continue;
				}

				if (!reader.TrySkip())
				{
					state = reader.CurrentState;
					consumed = reader.Position;
					return false;
				}
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

	private static async Task<(JsonReaderState State, string? Id, bool? IsRunning)> AdvanceToValuesArrayFromAsyncCursorAsync(
		IAsyncBufferCursor cursor,
		JsonReaderState state,
		CancellationToken ct)
	{
		string? id = null;
		bool? isRunning = null;

		while (await cursor.ReadAsync(ct).ConfigureAwait(false))
		{
			var buffer = cursor.Buffer;

			if (TryAdvanceToValuesArray(buffer, cursor.IsCompleted, ref state, out var consumed, ref id, ref isRunning))
			{
				cursor.AdvanceTo(consumed, buffer.End);
				return (state, id, isRunning);
			}

			cursor.AdvanceTo(consumed, buffer.End);

			if (cursor.IsCompleted)
				break;
		}

		throw new JsonException("ES|QL response does not contain a \"values\" property.");
	}

	private static (JsonReaderState State, string? Id, bool? IsRunning) AdvanceToValuesArrayFromSyncCursor(
		ISyncBufferCursor cursor,
		JsonReaderState state)
	{
		string? id = null;
		bool? isRunning = null;

		while (cursor.Read())
		{
			var buffer = cursor.Buffer;

			if (TryAdvanceToValuesArray(buffer, cursor.IsCompleted, ref state, out var consumed, ref id, ref isRunning))
			{
				cursor.AdvanceTo(consumed, buffer.End);
				return (state, id, isRunning);
			}

			cursor.AdvanceTo(consumed, buffer.End);
		}

		throw new JsonException("ES|QL response does not contain a \"values\" property.");
	}

	private static bool TryAdvanceToValuesArray(
		ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		out SequencePosition consumed,
		ref string? id,
		ref bool? isRunning)
	{
		var reader = new Utf8JsonReader(buffer, isFinalBlock, state);

		while (reader.Read())
		{
			if (reader.TokenType != JsonTokenType.PropertyName)
				continue;

			if (reader.ValueTextEquals("values"u8))
			{
				if (!reader.Read())
					break;

				state = reader.CurrentState;
				consumed = reader.Position;
				return true;
			}

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

	private static bool TryScanForId(
		ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		out SequencePosition consumed,
		out string? id,
		out bool reachedEnd)
	{
		id = null;
		reachedEnd = false;
		var reader = new Utf8JsonReader(buffer, isFinalBlock, state);

		while (reader.Read())
		{
			if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("id"u8))
			{
				if (reader.Read())
				{
					id = reader.GetString();
					state = reader.CurrentState;
					consumed = reader.Position;
					return true;
				}
			}

			if (reader.CurrentDepth == 0 && reader.TokenType == JsonTokenType.EndObject)
			{
				reachedEnd = true;
				state = reader.CurrentState;
				consumed = reader.Position;
				return true;
			}

			if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName)
			{
				if (!reader.TrySkip())
					break;
			}
		}

		state = reader.CurrentState;
		consumed = buffer.Start;
		return false;
	}

	private static async Task<(string? Id, JsonReaderState State)> ScanForIdAsync(
		IAsyncBufferCursor cursor,
		JsonReaderState state,
		CancellationToken ct)
	{
		while (await cursor.ReadAsync(ct).ConfigureAwait(false))
		{
			if (TryScanForId(cursor.Buffer, cursor.IsCompleted, ref state, out var consumed, out var id, out var reachedEnd))
			{
				cursor.AdvanceTo(consumed, cursor.Buffer.End);
				return (reachedEnd ? null : id, state);
			}

			cursor.AdvanceTo(consumed, cursor.Buffer.End);

			if (cursor.IsCompleted)
				break;
		}

		return (null, state);
	}

	private static (string? Id, JsonReaderState State) ScanForId(
		ISyncBufferCursor cursor,
		JsonReaderState state)
	{
		while (cursor.Read())
		{
			if (TryScanForId(cursor.Buffer, cursor.IsCompleted, ref state, out var consumed, out var id, out var reachedEnd))
			{
				cursor.AdvanceTo(consumed, cursor.Buffer.End);
				return (reachedEnd ? null : id, state);
			}

			cursor.AdvanceTo(consumed, cursor.Buffer.End);
		}

		return (null, state);
	}

	private static bool AdvancePastStartArray(ISyncBufferCursor cursor, ref JsonReaderState state)
	{
		while (cursor.Read())
		{
			var buffer = cursor.Buffer;
			var reader = new Utf8JsonReader(buffer, cursor.IsCompleted, state);

			if (!reader.Read())
			{
				state = reader.CurrentState;
				cursor.AdvanceTo(buffer.Start, buffer.End);
				continue;
			}

			if (reader.TokenType == JsonTokenType.StartArray)
			{
				state = reader.CurrentState;
				cursor.AdvanceTo(reader.Position, buffer.End);
				return true;
			}

			state = reader.CurrentState;
			cursor.AdvanceTo(reader.Position, buffer.End);
		}
		return false;
	}
}
