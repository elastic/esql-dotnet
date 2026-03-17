// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

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

	public IEsqlResponse ExecuteQuery(string esql, EsqlQueryOptions? options) => throw NotSupported();

	public Task<IEsqlAsyncResponse> ExecuteQueryAsync(string esql, EsqlQueryOptions? options, CancellationToken cancellationToken) =>
		throw NotSupported();

	public IEsqlResponse SubmitAsyncQuery(string esql, EsqlAsyncQueryOptions? options) =>
		throw NotSupported();

	public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(string esql, EsqlAsyncQueryOptions? options, CancellationToken cancellationToken) =>
		throw NotSupported();

	public IEsqlResponse PollAsyncQuery(string queryId) => throw NotSupported();

	public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, CancellationToken cancellationToken) =>
		throw NotSupported();

	public void DeleteAsyncQuery(string queryId) => throw NotSupported();

	public Task DeleteAsyncQueryAsync(string queryId, CancellationToken cancellationToken) => throw NotSupported();
}
