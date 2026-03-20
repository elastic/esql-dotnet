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
}
