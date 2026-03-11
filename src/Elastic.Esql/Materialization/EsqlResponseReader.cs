// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Collections.Concurrent;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;

namespace Elastic.Esql.Materialization;

internal abstract class EsqlResponseState
{
	private string? _id;
	private bool _isRunning;

	/// <summary>Current best-effort metadata snapshot. May be incomplete until rows are fully consumed.</summary>
	public EsqlStreamMetadata Metadata => new(_id, _isRunning);

	internal void SetId(string? id) => _id = id;

	internal void SetIsRunning(bool isRunning) => _isRunning = isRunning;
}

/// <summary>
/// Holds metadata and a lazy row stream from <see cref="EsqlResponseReader.ReadRowsWithMetadataAsync{T}(Stream, CancellationToken)"/>.
/// <para>
/// Metadata is <b>progressively populated</b> as the JSON is parsed. Properties found before or during column parsing are available immediately.
/// Properties that appear after the <c>values</c> array in the JSON are only captured after <see cref="Rows"/> is fully consumed.
/// </para>
/// </summary>
internal sealed class EsqlAsyncResponse<T> : EsqlResponseState
{
	/// <summary>Lazy row stream. Single-consume - the underlying response stream can only be read once.</summary>
	public IAsyncEnumerable<T> Rows { get; internal set; } = EmptyAsyncEnumerable<T>.Instance;
}

/// <summary>
/// Holds metadata and a lazy row sequence from <see cref="EsqlResponseReader.ReadRowsWithMetadata{T}(Stream)"/>.
/// <para>
/// Metadata is <b>progressively populated</b> as the JSON is parsed. Properties found before or during column parsing are available immediately.
/// Properties that appear after the <c>values</c> array in the JSON are only captured after <see cref="Rows"/> is fully consumed.
/// </para>
/// </summary>
internal sealed class EsqlResponse<T> : EsqlResponseState
{
	/// <summary>Lazy row sequence. Single-consume - the underlying response stream can only be read once.</summary>
	public IEnumerable<T> Rows { get; internal set; } = [];
}

/// <summary>
/// Streams ES|QL row-oriented (<c>columnar=false</c>) JSON responses into <c>T</c> instances with minimal allocations.
/// </summary>
internal sealed partial class EsqlResponseReader
{
	private static readonly JsonWriterOptions SkipValidationWriterOptions = new() { SkipValidation = true };
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

	private interface IBufferCursor
	{
		ReadOnlySequence<byte> Buffer { get; }
		bool IsCompleted { get; }
		void AdvanceTo(SequencePosition consumed, SequencePosition examined);
	}

	private interface IAsyncBufferCursor : IBufferCursor
	{
		ValueTask<bool> ReadAsync(CancellationToken cancellationToken);
	}

	private interface ISyncBufferCursor : IBufferCursor
	{
		bool Read();
	}

	private sealed class AsyncStreamBufferCursor(AsyncStreamBuffer asyncBuffer) : IAsyncBufferCursor
	{
		public ReadOnlySequence<byte> Buffer => asyncBuffer.Buffer;

		public bool IsCompleted => asyncBuffer.IsCompleted;

		public ValueTask<bool> ReadAsync(CancellationToken cancellationToken) =>
			asyncBuffer.ReadAsync(cancellationToken);

		public void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
			asyncBuffer.AdvanceTo(consumed, examined);
	}

	private sealed class SyncStreamBufferCursor(SyncStreamBuffer syncBuffer) : ISyncBufferCursor
	{
		public ReadOnlySequence<byte> Buffer => syncBuffer.Buffer;

		public bool IsCompleted => syncBuffer.IsCompleted;

		public bool Read() => syncBuffer.Read();

		public void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
			syncBuffer.AdvanceTo(consumed, examined);
	}

#if NET10_0_OR_GREATER
	private sealed class PipeReaderCursor(PipeReader pipeReader) : IAsyncBufferCursor
	{
		private ReadResult _result;

		public ReadOnlySequence<byte> Buffer => _result.Buffer;

		public bool IsCompleted => _result.IsCompleted;

		public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
		{
			_result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
			return !_result.Buffer.IsEmpty || !_result.IsCompleted;
		}

		public void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
			pipeReader.AdvanceTo(consumed, examined);
	}
#endif

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

	private static void ApplyMetadata(EsqlResponseState result, string? id, bool? isRunning)
	{
		if (id is not null)
			result.SetId(id);
		if (isRunning.HasValue)
			result.SetIsRunning(isRunning.Value);
	}

	private readonly record struct RowMaterializationPlan<T>(int EstimatedRowSize, bool IsScalar, JsonTypeInfo<T>? TypeInfo);

	private static RowMaterializationPlan<T> CreateRowMaterializationPlan<T>(ColumnInfo[] columns, JsonSerializerOptions options)
	{
		var estimatedRowSize = Math.Max(256, columns.Length * 32);
		var isScalar = columns.Length == 1 && IsPrimitiveJsonType(typeof(T));
		var typeInfo = TryResolveTypeInfo<T>(options);
		return new RowMaterializationPlan<T>(estimatedRowSize, isScalar, typeInfo);
	}

	private static bool IsPrimitiveJsonType(Type type)
	{
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t.IsPrimitive || t == typeof(decimal) || t == typeof(string) || t.IsEnum;
	}

	private async Task<(ColumnInfo[] Columns, JsonReaderState ReaderState, ColumnLayout Layout)> PrepareRowsAsync<T>(
		IAsyncBufferCursor cursor,
		CancellationToken cancellationToken)
	{
		var (columns, readerState, consumed, _, _) = await ReadColumnsFromAsyncCursorAsync(cursor, cancellationToken).ConfigureAwait(false);
		cursor.AdvanceTo(consumed, cursor.Buffer.End);

		(readerState, _, _) = await AdvanceToValuesArrayFromAsyncCursorAsync(cursor, readerState, cancellationToken).ConfigureAwait(false);
		return (columns, readerState, GetColumnLayout<T>(columns));
	}

	private (ColumnInfo[] Columns, JsonReaderState ReaderState, ColumnLayout Layout) PrepareRows<T>(ISyncBufferCursor cursor)
	{
		var (columns, readerState, consumed, _, _) = ReadColumnsFromSyncCursor(cursor);
		cursor.AdvanceTo(consumed, cursor.Buffer.End);

		(readerState, _, _) = AdvanceToValuesArrayFromSyncCursor(cursor, readerState);
		return (columns, readerState, GetColumnLayout<T>(columns));
	}

	private static async Task<(ColumnInfo[] Columns, JsonReaderState State, SequencePosition Consumed, string? Id, bool? IsRunning)> ReadColumnsFromAsyncCursorAsync(
		IAsyncBufferCursor cursor,
		CancellationToken ct)
	{
		var state = new JsonReaderState();
		string? id = null;
		bool? isRunning = null;

		while (await cursor.ReadAsync(ct).ConfigureAwait(false))
		{
			var buffer = cursor.Buffer;

			if (TryParseColumns(buffer, cursor.IsCompleted, ref state, out var columns, out var consumed, ref id, ref isRunning))
				return (columns!, state, consumed, id, isRunning);

			cursor.AdvanceTo(buffer.Start, buffer.End);

			if (cursor.IsCompleted)
				break;
		}

		throw new JsonException("Stream ended before \"columns\" array was fully read.");
	}

	private static (ColumnInfo[] Columns, JsonReaderState State, SequencePosition Consumed, string? Id, bool? IsRunning) ReadColumnsFromSyncCursor(
		ISyncBufferCursor cursor)
	{
		var state = new JsonReaderState();
		string? id = null;
		bool? isRunning = null;

		while (cursor.Read())
		{
			var buffer = cursor.Buffer;

			if (TryParseColumns(buffer, cursor.IsCompleted, ref state, out var columns, out var consumed, ref id, ref isRunning))
				return (columns!, state, consumed, id, isRunning);

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
}

internal sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
{
	public static readonly EmptyAsyncEnumerable<T> Instance = new();

	public T Current => default!;

	public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

	public ValueTask<bool> MoveNextAsync() => new(false);

	public ValueTask DisposeAsync() => default;
}
