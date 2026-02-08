// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;
using Elastic.Mapping;

namespace Elastic.Esql.Core;

/// <summary>
/// Context for ES|QL query execution.
/// </summary>
/// <remarks>
/// Creates a new query context.
/// </remarks>
public class EsqlQueryContext(IElasticsearchMappingContext? mappingContext = null, IEsqlQueryExecutor? executor = null)
{
	/// <summary>
	/// The query executor. Null means translation-only mode.
	/// </summary>
	public IEsqlQueryExecutor? Executor { get; } = executor;

	/// <summary>
	/// The metadata resolver for field name and type resolution.
	/// </summary>
	public TypeFieldMetadataResolver MetadataResolver { get; } = new(mappingContext);

	/// <summary>
	/// Explicit index pattern override. When set, this takes precedence over the type's EsqlIndex attribute.
	/// </summary>
	public string? IndexPattern { get; set; }

	/// <summary>
	/// The cancellation token for query execution.
	/// </summary>
	public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

	/// <summary>
	/// Per-query options that override client defaults.
	/// </summary>
	public EsqlQueryOptions? QueryOptions { get; set; }

	/// <summary>
	/// When non-null, captured variables are emitted as named parameters instead of inlined values.
	/// </summary>
	public EsqlParameters? ParameterCollection { get; set; }
}
