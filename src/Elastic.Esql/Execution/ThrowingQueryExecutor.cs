// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Execution;

/// <summary>
/// Default <see cref="IEsqlQueryExecutor"/> that throws on every operation.
/// Used by translation-only providers that do not support query execution.
/// </summary>
internal sealed class ThrowingQueryExecutor : IEsqlQueryExecutor
{
	public static readonly ThrowingQueryExecutor Instance = new();

	private ThrowingQueryExecutor() { }

	private static InvalidOperationException NotSupported() =>
		new("This provider does not support query execution. Supply an IEsqlQueryExecutor to enable execution.");

	public IEsqlResponse ExecuteQuery(string esql, EsqlParameters? parameters, object? options) => throw NotSupported();

	public Task<IEsqlAsyncResponse> ExecuteQueryAsync(string esql, EsqlParameters? parameters, object? options, CancellationToken cancellationToken) =>
		throw NotSupported();

	public IEsqlResponse SubmitAsyncQuery(string esql, EsqlParameters? parameters, object? options, EsqlAsyncQueryOptions? asyncOptions) =>
		throw NotSupported();

	public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(string esql, EsqlParameters? parameters, object? options, EsqlAsyncQueryOptions? asyncOptions, CancellationToken cancellationToken) =>
		throw NotSupported();

	public IEsqlResponse PollAsyncQuery(string queryId, object? options) => throw NotSupported();

	public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, object? options, CancellationToken cancellationToken) =>
		throw NotSupported();

	public void DeleteAsyncQuery(string queryId, object? options) => throw NotSupported();

	public Task DeleteAsyncQueryAsync(string queryId, object? options, CancellationToken cancellationToken) => throw NotSupported();
}
