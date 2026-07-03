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

	public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request) => throw NotSupported();

	public Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) => throw NotSupported();

	public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request) => throw NotSupported();

	public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) => throw NotSupported();

	public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request) => throw NotSupported();

	public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) =>
		throw NotSupported();

	public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request) => throw NotSupported();

	public Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) => throw NotSupported();
}
