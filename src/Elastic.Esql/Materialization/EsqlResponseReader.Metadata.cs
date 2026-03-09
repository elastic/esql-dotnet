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
		var pipeReader = CreatePipeReader(stream);

		try
		{
			return await ReadMetadataAsync(pipeReader, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}
	}

	/// <inheritdoc cref="ReadMetadataAsync(Stream, CancellationToken)"/>
	public static async Task<EsqlStreamMetadata> ReadMetadataAsync(
		PipeReader pipeReader,
		CancellationToken cancellationToken = default)
	{
		var state = new JsonReaderState();
		string? id = null;
		var isRunning = false;
		var depth0Entered = false;

		while (true)
		{
			var result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
			var buffer = result.Buffer;

			if (TryParseMetadata(buffer, result.IsCompleted, ref state, ref id, ref isRunning, ref depth0Entered, out var consumed))
			{
				pipeReader.AdvanceTo(consumed, buffer.End);
				return new EsqlStreamMetadata(id, isRunning);
			}

			pipeReader.AdvanceTo(consumed, buffer.End);

			if (result.IsCompleted)
				return new EsqlStreamMetadata(id, isRunning);
		}
	}

	/// <summary>
	/// Reads only the async query metadata (<c>id</c>, <c>is_running</c>) from an ES|QL response stream.
	/// Skips <c>columns</c> and <c>values</c> arrays entirely via <see cref="Utf8JsonReader.TrySkip"/>,
	/// making this very lightweight. Guarantees metadata is fully resolved regardless of JSON property order.
	/// </summary>
	public static EsqlStreamMetadata ReadMetadata(Stream stream)
	{
		using var syncBuffer = new SyncStreamBuffer(stream);

		var state = new JsonReaderState();
		string? id = null;
		var isRunning = false;
		var depth0Entered = false;

		while (syncBuffer.Read())
		{
			var buffer = syncBuffer.Buffer;

			if (TryParseMetadata(buffer, syncBuffer.IsCompleted, ref state, ref id, ref isRunning, ref depth0Entered, out var consumed))
			{
				syncBuffer.AdvanceTo(consumed, buffer.End);
				return new EsqlStreamMetadata(id, isRunning);
			}

			syncBuffer.AdvanceTo(consumed, buffer.End);
		}

		return new EsqlStreamMetadata(id, isRunning);
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
}
