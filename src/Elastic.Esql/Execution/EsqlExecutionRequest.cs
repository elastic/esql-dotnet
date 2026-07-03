// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Execution;

/// <summary>
/// Carries all inputs an <see cref="IEsqlQueryExecutor"/> needs to issue an ES|QL request.
/// New protocol knobs become init-only properties here so that adding one does not break executor implementations.
/// </summary>
public sealed record EsqlExecutionRequest
{
	/// <summary>The ES|QL query text. For poll and delete calls this is the originally submitted query text and is not sent.</summary>
	public required string Esql { get; init; }

	/// <summary>Named query parameters, or <c>null</c> when the query has none.</summary>
	public EsqlParameters? Parameters { get; init; }

	/// <summary>Protocol-level query options, or <c>null</c> to use server defaults.</summary>
	public EsqlQueryOptions? QueryOptions { get; init; }

	/// <summary>Opaque executor-specific options (e.g. transport request configuration), or <c>null</c>.</summary>
	public object? ExecutorOptions { get; init; }

	/// <summary>Async submission behavior. Only used by <see cref="IEsqlQueryExecutor.SubmitAsyncQuery"/> and <see cref="IEsqlQueryExecutor.SubmitAsyncQueryAsync"/>.</summary>
	public EsqlAsyncQueryOptions? AsyncOptions { get; init; }

	/// <summary>The wire-level response format. <c>null</c> means the default JSON path with typed materialization.</summary>
	public EsqlFormat? Format { get; init; }
}
