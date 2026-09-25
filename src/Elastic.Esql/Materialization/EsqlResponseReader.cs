// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Collections.Concurrent;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Streams ES|QL row-oriented (<c>columnar=false</c>) JSON responses into <c>T</c> instances with minimal allocations.
/// </summary>
internal sealed partial class EsqlResponseReader
{
	private readonly JsonMetadataManager _metadata;
	private readonly ConcurrentDictionary<ColumnLayoutCacheKey, ColumnLayoutCacheEntry> _columnLayoutCache = [];

	/// <summary>The <see cref="JsonSerializerOptions"/> used for deserialization.</summary>
	public JsonSerializerOptions Options => _metadata.Options;

	internal EsqlResponseReader(JsonMetadataManager metadata, IReadOnlyList<SourceMember>? sourceMembers = null)
	{
		_metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
		_sourceMembers = sourceMembers ?? [];
	}

	private readonly IReadOnlyList<SourceMember> _sourceMembers;

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

	/// <summary>
	/// Cursor over an already-drained response region. Exposes the remaining bytes directly instead of
	/// re-copying them through a stream and a second pooled buffer.
	/// </summary>
	private sealed class DrainedBufferCursor(byte[] buffer, int start, int end) : ISyncBufferCursor
	{
		private int _consumed = start;

		public ReadOnlySequence<byte> Buffer => new(buffer, _consumed, end - _consumed);

		// The entire payload is in memory, so the exposed buffer is always the final block.
		public bool IsCompleted => true;

		// The entire payload is already drained into memory, so end of data is always reached.
		public bool IsEofReached => true;

		public bool Read() => _consumed < end;

		// Positions originate from Buffer, a single-segment sequence over the backing array, so
		// GetInteger() is the absolute array index (the same contract StreamBuffer relies on).
		public void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
			_consumed = consumed.GetInteger();
	}

#if NET10_0_OR_GREATER
	private sealed class PipeReaderCursor(PipeReader pipeReader) : IAsyncBufferCursor
	{
		private ReadResult _result;

		public ReadOnlySequence<byte> Buffer => _result.Buffer;

		public bool IsCompleted => _result.IsCompleted;

		// For a pipe, a completed read result already means no more data will arrive.
		public bool IsEofReached => _result.IsCompleted;

		[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
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
		JsonMetadataManager.TryMakeReadOnly(Options);
		var targetType = typeof(T);
		var schemaHash = ComputeSchemaHash(columns);
		var key = new ColumnLayoutCacheKey(targetType, schemaHash, columns.Length);

		if (_columnLayoutCache.TryGetValue(key, out var cachedEntry) && cachedEntry.Matches(columns))
			return cachedEntry.Layout;

		var layout = ColumnLayout.Build(columns, targetType, _metadata, _sourceMembers);
		// Mutable options resolve contracts the serializer does not keep, so a layout built on them is not reused.
		if (Options.IsReadOnly)
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

	/// <summary>
	/// Resolves <see cref="JsonTypeInfo{T}"/> for <c>List&lt;T&gt;</c>, enabling batched row deserialization.
	/// Returns <see langword="null"/> when the configured resolver has no metadata for <c>List&lt;T&gt;</c>
	/// (e.g. a source-generated context without a <c>List&lt;T&gt;</c> registration); callers must then use
	/// the per-row typed path so AOT never silently falls back to reflection.
	/// </summary>
	private static JsonTypeInfo<List<T>>? TryResolveListTypeInfo<T>(JsonSerializerOptions options) =>
		// A user converter for List<T> would see the internal batch wrapper instead of the caller's rows, so only the
		// framework's own list converter keeps batching transparent.
		JsonMetadataManager.TryResolveTypeInfo(typeof(List<T>), options) is { } typeInfo && JsonMetadataManager.IsBuiltInConverter(typeInfo.Converter)
			? typeInfo as JsonTypeInfo<List<T>>
			: null;

	private readonly record struct RowMaterializationPlan<T>(
		int EstimatedRowSize,
		bool IsScalar,
		JsonTypeInfo<T>? TypeInfo,
		JsonSerializerOptions Options,
		DirectBinderKind? ScalarKind,
		bool WrapScalarInArray)
	{
		public RowAssemblyBuffers CreateAssemblyBuffers() => new(EstimatedRowSize, IsScalar, WrapScalarInArray);
	}

	private static RowMaterializationPlan<T> CreateRowMaterializationPlan<T>(ColumnLayout layout, JsonSerializerOptions options)
	{
		var estimatedRowSize = Math.Max(256, layout.ColumnCount * 32);
		// Null when T has no metadata: rows then deserialize through the non-generic options-based overload.
		var typeInfo = JsonMetadataManager.TryResolveTypeInfo(typeof(T), options);
		var isScalar = layout.ColumnCount == 1 && IsScalarTarget(typeof(T), typeInfo);
		var scalarKind = isScalar ? layout.ScalarKind : null;

		// A collection target takes the whole cell, so a single-valued multi-value column needs the same
		// wrap the row path applies to a collection property.
		var wrapScalarInArray = isScalar && ColumnLayout.IsCollectionContract(typeof(T), typeInfo, propertyConverter: null, options);

		return new RowMaterializationPlan<T>(estimatedRowSize, isScalar, typeInfo as JsonTypeInfo<T>, options, scalarKind, wrapScalarInArray);
	}

	/// <summary>
	/// A single-column response binds the cell itself to <c>T</c>, unless <c>T</c> is a document-like target, an object
	/// or dictionary contract whose one property (or key) is what the column maps to, or a type without metadata that
	/// is outside the single-value list the translation of a single-value Select uses.
	/// </summary>
	private static bool IsScalarTarget(Type type, JsonTypeInfo? typeInfo) =>
		!IsDocumentTarget(type)
		&& (typeInfo is null ? TypeHelper.IsSingleValueType(type) : typeInfo.Kind is JsonTypeInfoKind.None or JsonTypeInfoKind.Enumerable);

	/// <summary>
	/// A document-like target reads the whole row whatever the column count, so a one-column query keeps the shape of a wider one.
	/// <c>JsonArray</c> and <c>JsonValue</c> cannot hold the row object, so they bind the cell.
	/// </summary>
	private static bool IsDocumentTarget(Type type)
	{
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t == typeof(object) || t == typeof(JsonDocument) || t == typeof(JsonElement) || t == typeof(JsonNode) || t == typeof(JsonObject);
	}
}
