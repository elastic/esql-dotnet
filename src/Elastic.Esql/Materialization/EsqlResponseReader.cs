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
	private bool _optionsFrozen;

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
		// GetInteger() is the absolute array index (the same contract SyncStreamBuffer relies on).
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

	private void EnsureOptionsReadOnly()
	{
		if (_optionsFrozen)
			return;
		// Mutable options re-resolve a fresh contract on every GetTypeInfo call;
		// frozen options reuse the cache at near-zero cost. STJ requires a
		// TypeInfoResolver to be set before accepting MakeReadOnly.
		if (!Options.IsReadOnly && Options.TypeInfoResolver is not null)
			Options.MakeReadOnly();
		_optionsFrozen = true;
	}

	/// <summary>
	/// Builds a <see cref="ColumnLayout"/> for the target type and the ES|QL columns.
	/// </summary>
	private ColumnLayout GetColumnLayout<T>(ColumnInfo[] columns)
	{
		EnsureOptionsReadOnly();
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

	private static JsonTypeInfo? TryResolveTypeInfo(Type type, JsonSerializerOptions options)
	{
		try
		{
			return options.GetTypeInfo(type);
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// No metadata for T: rows deserialize through the non-generic options-based overload instead.
			return null;
		}
	}

	/// <summary>
	/// Resolves <see cref="JsonTypeInfo{T}"/> for <c>List&lt;T&gt;</c>, enabling batched row deserialization.
	/// Returns <see langword="null"/> when the configured resolver has no metadata for <c>List&lt;T&gt;</c>
	/// (e.g. a source-generated context without a <c>List&lt;T&gt;</c> registration); callers must then use
	/// the per-row typed path so AOT never silently falls back to reflection.
	/// </summary>
	private static JsonTypeInfo<List<T>>? TryResolveListTypeInfo<T>(JsonSerializerOptions options)
	{
		try
		{
			// TryGetTypeInfo answers "not registered" without an exception, which matters for
			// source-generated contexts that omit List<T>: this runs once per enumeration. A user
			// converter for List<T> would see the internal batch wrapper instead of the caller's
			// rows, so only the framework's own list converter keeps batching transparent.
			return options.TryGetTypeInfo(typeof(List<T>), out var typeInfo)
				&& typeInfo.Converter.GetType().Assembly == typeof(JsonSerializerOptions).Assembly
				? typeInfo as JsonTypeInfo<List<T>>
				: null;
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// A resolver that throws for List<T> (for example the reflection fallback under Native AOT) means: fall back to the per-row path.
			return null;
		}
	}

	private readonly record struct RowMaterializationPlan<T>(
		int EstimatedRowSize,
		bool IsScalar,
		JsonTypeInfo<T>? TypeInfo,
		JsonSerializerOptions Options,
		DirectBinderKind? ScalarKind,
		bool WrapScalarInArray);

	private static RowMaterializationPlan<T> CreateRowMaterializationPlan<T>(ColumnInfo[] columns, JsonSerializerOptions options)
	{
		var estimatedRowSize = Math.Max(256, columns.Length * 32);
		var typeInfo = TryResolveTypeInfo(typeof(T), options);
		var isScalar = columns.Length == 1 && IsScalarTarget(typeof(T), typeInfo);
		var scalarKind = isScalar && DirectRowBinder.TryClassifyScalar(typeof(T), typeInfo, options, out var kind) ? kind : (DirectBinderKind?)null;

		// A collection target takes the whole cell, so a single-valued multi-value column needs the same
		// wrap the row path applies to a collection property.
		var wrapScalarInArray = isScalar && typeInfo?.Kind == JsonTypeInfoKind.Enumerable;

		return new RowMaterializationPlan<T>(estimatedRowSize, isScalar, typeInfo as JsonTypeInfo<T>, options, scalarKind, wrapScalarInArray);
	}

	/// <summary>
	/// A single-column response binds the cell itself to <c>T</c>, unless <c>T</c> is an object or dictionary
	/// contract whose one property (or key) is what the column maps to.
	/// </summary>
	private static bool IsScalarTarget(Type type, JsonTypeInfo? typeInfo) =>
		typeInfo is null
			? IsPrimitiveJsonType(type)
			: typeInfo.Kind is JsonTypeInfoKind.None or JsonTypeInfoKind.Enumerable;

	private static bool IsPrimitiveJsonType(Type type)
	{
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t.IsPrimitive || t == typeof(decimal) || t == typeof(string) || t.IsEnum;
	}
}
