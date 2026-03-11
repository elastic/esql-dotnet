// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text.Json;

namespace Elastic.Esql.Materialization;

/// <summary>
/// ES|QL async query response metadata (<c>id</c> and <c>is_running</c>).
/// </summary>
internal readonly record struct EsqlStreamMetadata(string? Id, bool IsRunning);

internal sealed partial class EsqlResponseReader
{
	/// <summary>
	/// Reads only the async query metadata (<c>id</c>, <c>is_running</c>) from an ES|QL response stream.
	/// Skips <c>columns</c> and <c>values</c> arrays entirely via <see cref="Utf8JsonReader.TrySkip"/>,
	/// making this very lightweight. Guarantees metadata is fully resolved regardless of JSON property order.
	/// </summary>
	public static async Task<EsqlStreamMetadata> ReadMetadataAsync(
		Stream stream,
		CancellationToken cancellationToken = default)
	{
		using var asyncBuffer = new AsyncStreamBuffer(stream);
		var cursor = new AsyncStreamBufferCursor(asyncBuffer);
		return await ReadMetadataAsync(cursor, cancellationToken).ConfigureAwait(false);
	}

#if NET10_0_OR_GREATER
	/// <inheritdoc cref="ReadMetadataAsync(Stream, CancellationToken)"/>
	public static Task<EsqlStreamMetadata> ReadMetadataAsync(
		PipeReader pipeReader,
		CancellationToken cancellationToken = default) =>
		ReadMetadataAsync(new PipeReaderCursor(pipeReader), cancellationToken);
#endif

	/// <summary>
	/// Reads only the async query metadata (<c>id</c>, <c>is_running</c>) from an ES|QL response stream.
	/// Skips <c>columns</c> and <c>values</c> arrays entirely via <see cref="Utf8JsonReader.TrySkip"/>,
	/// making this very lightweight. Guarantees metadata is fully resolved regardless of JSON property order.
	/// </summary>
	public static EsqlStreamMetadata ReadMetadata(Stream stream)
	{
		using var syncBuffer = new SyncStreamBuffer(stream);
		var cursor = new SyncStreamBufferCursor(syncBuffer);
		return ReadMetadata(cursor);
	}

	private static async Task<EsqlStreamMetadata> ReadMetadataAsync(
		IAsyncBufferCursor cursor,
		CancellationToken cancellationToken)
	{
		var state = new JsonReaderState();
		string? id = null;
		var isRunning = false;
		var depth0Entered = false;

		while (await cursor.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var buffer = cursor.Buffer;

			if (TryParseMetadata(buffer, cursor.IsCompleted, ref state, ref id, ref isRunning, ref depth0Entered, out var consumed))
			{
				cursor.AdvanceTo(consumed, buffer.End);
				return new EsqlStreamMetadata(id, isRunning);
			}

			cursor.AdvanceTo(consumed, buffer.End);

			if (cursor.IsCompleted)
				break;
		}

		return new EsqlStreamMetadata(id, isRunning);
	}

	private static EsqlStreamMetadata ReadMetadata(
		ISyncBufferCursor cursor)
	{
		var state = new JsonReaderState();
		string? id = null;
		var isRunning = false;
		var depth0Entered = false;

		while (cursor.Read())
		{
			var buffer = cursor.Buffer;

			if (TryParseMetadata(buffer, cursor.IsCompleted, ref state, ref id, ref isRunning, ref depth0Entered, out var consumed))
			{
				cursor.AdvanceTo(consumed, buffer.End);
				return new EsqlStreamMetadata(id, isRunning);
			}

			cursor.AdvanceTo(consumed, buffer.End);
		}

		return new EsqlStreamMetadata(id, isRunning);
	}

	/// <summary>
	/// After all rows have been consumed, scans remaining top-level JSON properties
	/// for <c>id</c> and <c>is_running</c> that may appear after the <c>values</c> array.
	/// </summary>
	private static async Task ScanRemainingMetadataAsync<T>(
		IAsyncBufferCursor cursor,
		JsonReaderState state,
		EsqlAsyncResponse<T> result,
		CancellationToken ct)
	{
		string? id = null;
		bool? isRunning = null;

		while (await cursor.ReadAsync(ct).ConfigureAwait(false))
		{
			var buffer = cursor.Buffer;
			if (buffer.IsEmpty && cursor.IsCompleted)
				break;

			if (TryScanRemainingProperties(buffer, cursor.IsCompleted, ref state, out var consumed, ref id, ref isRunning, out _))
			{
				cursor.AdvanceTo(consumed, buffer.End);
				break;
			}

			cursor.AdvanceTo(consumed, buffer.End);

			if (cursor.IsCompleted)
				break;
		}

		ApplyMetadata(result, id, isRunning);
	}

	private static void ScanRemainingMetadata<T>(
		ISyncBufferCursor cursor,
		JsonReaderState state,
		EsqlResponse<T> result)
	{
		string? id = null;
		bool? isRunning = null;

		while (cursor.Read() || !cursor.IsCompleted)
		{
			var buffer = cursor.Buffer;
			if (buffer.IsEmpty && cursor.IsCompleted)
				break;

			if (TryScanRemainingProperties(buffer, cursor.IsCompleted, ref state, out var consumed, ref id, ref isRunning, out _))
			{
				cursor.AdvanceTo(consumed, buffer.End);
				break;
			}

			cursor.AdvanceTo(consumed, buffer.End);

			if (cursor.IsCompleted)
				break;
		}

		ApplyMetadata(result, id, isRunning);
	}

	private static bool TryParseMetadata(
		ReadOnlySequence<byte> buffer,
		bool isFinalBlock,
		ref JsonReaderState state,
		ref string? id,
		ref bool isRunning,
		ref bool depth0Entered,
		out SequencePosition consumed)
	{
		var reader = new Utf8JsonReader(buffer, isFinalBlock, state);

		while (reader.Read())
		{
			if (!depth0Entered && reader.TokenType == JsonTokenType.StartObject)
			{
				depth0Entered = true;
				continue;
			}

			if (reader.CurrentDepth == 0 && reader.TokenType == JsonTokenType.EndObject)
			{
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
			}
			else if (reader.ValueTextEquals("is_running"u8))
			{
				if (!reader.Read())
					break;

				isRunning = reader.GetBoolean();
			}
			else
			{
				if (!reader.TrySkip())
					break;
			}
		}

		state = reader.CurrentState;
		consumed = reader.Position;
		return false;
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
}
