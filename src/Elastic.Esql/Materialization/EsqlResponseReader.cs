// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Holds metadata and a lazy row stream from <see cref="EsqlResponseReader.ReadRowsWithMetadataAsync{T}(Stream, CancellationToken)"/> /
/// <see cref="EsqlResponseReader.ReadRowsWithMetadataAsync{T}(PipeReader, CancellationToken)"/>.
/// <para>
/// Metadata is <b>progressively populated</b> as the JSON is parsed. Properties found before or during column parsing are available immediately.
/// Properties that appear after the <c>values</c> array in the JSON are only captured after <see cref="Rows"/> is fully consumed.
/// </para>
/// </summary>
internal sealed class EsqlAsyncResponse<T>
{
	private string? _id;
	private bool _isRunning;

	/// <summary>Current best-effort metadata snapshot. May be incomplete until <see cref="Rows"/> is fully consumed.</summary>
	public EsqlStreamMetadata Metadata => new(_id, _isRunning);

	/// <summary>Lazy row stream. Single-consume - the underlying response stream can only be read once.</summary>
	public IAsyncEnumerable<T> Rows { get; internal set; } = EmptyAsyncEnumerable<T>.Instance;

	internal void SetId(string? id) => _id = id;

	internal void SetIsRunning(bool isRunning) => _isRunning = isRunning;
}

/// <summary>
/// Holds metadata and a lazy row sequence from <see cref="EsqlResponseReader.ReadRowsWithMetadata{T}(Stream)"/>.
/// <para>
/// Metadata is <b>progressively populated</b> as the JSON is parsed. Properties found before or during column parsing are available immediately.
/// Properties that appear after the <c>values</c> array in the JSON are only captured after <see cref="Rows"/> is fully consumed.
/// </para>
/// </summary>
internal sealed class EsqlResponse<T>
{
	private string? _id;
	private bool _isRunning;

	/// <summary>Current best-effort metadata snapshot. May be incomplete until <see cref="Rows"/> is fully consumed.</summary>
	public EsqlStreamMetadata Metadata => new(_id, _isRunning);

	/// <summary>Lazy row sequence. Single-consume - the underlying response stream can only be read once.</summary>
	public IEnumerable<T> Rows { get; internal set; } = [];

	internal void SetId(string? id) => _id = id;

	internal void SetIsRunning(bool isRunning) => _isRunning = isRunning;
}

/// <summary>
/// Streams ES|QL row-oriented (<c>columnar=false</c>) JSON responses into <c>T</c> instances with minimal allocations.
/// </summary>
internal sealed partial class EsqlResponseReader
{
	private readonly JsonMetadataManager _metadata;
	private readonly ConcurrentDictionary<ColumnLayoutCacheKey, ColumnLayoutCacheEntry> _columnLayoutCache = [];

	/// <summary>The <see cref="JsonSerializerOptions"/> used for deserialization.</summary>
	public JsonSerializerOptions Options => _metadata.Options;

	internal EsqlResponseReader(JsonMetadataManager metadata) =>
		_metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

	internal readonly record struct ColumnInfo(string Name, string Type);
	private readonly record struct ColumnLayoutCacheKey(Type TargetType, int SchemaHash, int ColumnCount);

	private sealed class ColumnLayoutCacheEntry
	{
		private readonly ColumnInfo[] _columns;

		public ColumnLayout Layout { get; }

		public ColumnLayoutCacheEntry(ReadOnlySpan<ColumnInfo> columns, ColumnLayout layout)
		{
			_columns = columns.ToArray();
			Layout = layout;
		}

		public bool Matches(ReadOnlySpan<ColumnInfo> columns)
		{
			if (_columns.Length != columns.Length)
				return false;

			for (var i = 0; i < columns.Length; i++)
			{
				var candidate = columns[i];
				var cached = _columns[i];
				if (!string.Equals(cached.Name, candidate.Name, StringComparison.Ordinal))
					return false;
				if (!string.Equals(cached.Type, candidate.Type, StringComparison.Ordinal))
					return false;
			}

			return true;
		}
	}

	private static PipeReader CreatePipeReader(Stream stream) =>
		PipeReader.Create(stream, new StreamPipeReaderOptions(
			pool: MemoryPool<byte>.Shared,
			bufferSize: 16384,
			leaveOpen: true));

	private static OwnedPipeReader CreateOwnedPipeReader(Stream stream) =>
		new(CreatePipeReader(stream));

	private sealed class OwnedPipeReader(PipeReader reader) : IAsyncDisposable
	{
		public PipeReader Reader { get; } = reader;

		public ValueTask DisposeAsync() => Reader.CompleteAsync();
	}

	/// <summary>
	/// Builds a <see cref="ColumnLayout"/> for the target type and the ES|QL columns.
	/// </summary>
	private ColumnLayout GetColumnLayout<T>(ColumnInfo[] columns)
	{
		var targetType = typeof(T);
		var schemaHash = ComputeSchemaHash(columns);
		var key = new ColumnLayoutCacheKey(targetType, schemaHash, columns.Length);

		if (_columnLayoutCache.TryGetValue(key, out var cachedEntry) && cachedEntry.Matches(columns))
			return cachedEntry.Layout;

		var layout = ColumnLayout.Build(columns, targetType, _metadata);
		_columnLayoutCache[key] = new ColumnLayoutCacheEntry(columns, layout);
		return layout;
	}

	private static int ComputeSchemaHash(ReadOnlySpan<ColumnInfo> columns)
	{
		var hashCode = new HashCode();

		foreach (var column in columns)
		{
			hashCode.Add(column.Name, StringComparer.Ordinal);
			hashCode.Add(column.Type, StringComparer.Ordinal);
		}

		return hashCode.ToHashCode();
	}

	private static JsonTypeInfo<T>? TryResolveTypeInfo<T>(JsonSerializerOptions options)
	{
		try
		{
			return options.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Incrementally parses columns from the pipe, also capturing any <c>id</c>/<c>is_running</c> metadata properties encountered before the
	/// <c>columns</c> array.
	/// </summary>
	private static async Task<(ColumnInfo[] Columns, JsonReaderState State, SequencePosition Consumed, SequencePosition Examined, string? Id, bool? IsRunning)> ReadColumnsFromPipeAsync(
		PipeReader pipeReader,
		CancellationToken ct)
	{
		var state = new JsonReaderState();
		string? id = null;
		bool? isRunning = null;

		while (true)
		{
			var result = await pipeReader.ReadAsync(ct).ConfigureAwait(false);
			var buffer = result.Buffer;

			if (TryParseColumns(buffer, result.IsCompleted, ref state, out var columns, out var consumed, ref id, ref isRunning))
				return (columns!, state, consumed, buffer.End, id, isRunning);

			pipeReader.AdvanceTo(buffer.Start, buffer.End);

			if (result.IsCompleted)
				throw new JsonException("Stream ended before \"columns\" array was fully read.");
		}
	}

	private static (ColumnInfo[] Columns, JsonReaderState State, SequencePosition Consumed, string? Id, bool? IsRunning) ReadColumnsFromStream(
		SyncStreamBuffer syncBuffer)
	{
		var state = new JsonReaderState();
		string? id = null;
		bool? isRunning = null;

		while (syncBuffer.Read())
		{
			var buffer = syncBuffer.Buffer;

			if (TryParseColumns(buffer, syncBuffer.IsCompleted, ref state, out var columns, out var consumed, ref id, ref isRunning))
				return (columns!, state, consumed, id, isRunning);

			syncBuffer.AdvanceTo(buffer.Start, buffer.End);
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
		ref bool? isRunning)
	{
		columns = null;
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

	/// <summary>
	/// Advances past everything between <c>columns</c> and the start of the <c>values</c> array,
	/// also capturing any <c>id</c>/<c>is_running</c> metadata properties encountered along the way.
	/// </summary>
	private static async Task<(JsonReaderState State, string? Id, bool? IsRunning)> AdvanceToValuesArrayFromPipeAsync(
		PipeReader pipeReader,
		JsonReaderState state,
		CancellationToken ct)
	{
		string? id = null;
		bool? isRunning = null;

		while (true)
		{
			var result = await pipeReader.ReadAsync(ct).ConfigureAwait(false);
			var buffer = result.Buffer;

			if (TryAdvanceToValuesArray(buffer, result.IsCompleted, ref state, out var consumed, ref id, ref isRunning))
			{
				pipeReader.AdvanceTo(consumed, buffer.End);
				return (state, id, isRunning);
			}

			pipeReader.AdvanceTo(consumed, buffer.End);

			if (result.IsCompleted)
				throw new JsonException("ES|QL response does not contain a \"values\" property.");
		}
	}

	private static (JsonReaderState State, string? Id, bool? IsRunning) AdvanceToValuesArrayFromStream(
		SyncStreamBuffer syncBuffer,
		JsonReaderState state)
	{
		string? id = null;
		bool? isRunning = null;

		while (syncBuffer.Read())
		{
			var buffer = syncBuffer.Buffer;

			if (TryAdvanceToValuesArray(buffer, syncBuffer.IsCompleted, ref state, out var consumed, ref id, ref isRunning))
			{
				syncBuffer.AdvanceTo(consumed, buffer.End);
				return (state, id, isRunning);
			}

			syncBuffer.AdvanceTo(consumed, buffer.End);
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
}

internal sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
{
	public static readonly EmptyAsyncEnumerable<T> Instance = new();

	public T Current => default!;

	public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

	public ValueTask<bool> MoveNextAsync() => new(false);

	public ValueTask DisposeAsync() => default;
}
