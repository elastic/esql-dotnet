// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Execution;

/// <summary>
/// Abstracts the transport layer for ES|QL query execution.
/// Implementations issue HTTP requests and return raw response bodies
/// as <see cref="IEsqlResponse"/> (sync / <see cref="Stream"/>) or
/// <see cref="IEsqlAsyncResponse"/> (async / streamed response body).
/// </summary>
public interface IEsqlQueryExecutor
{
	/// <summary>Executes an ES|QL query synchronously.</summary>
	IEsqlResponse ExecuteQuery(EsqlExecutionRequest request);

	/// <summary>Executes an ES|QL query asynchronously.</summary>
	Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken);

	/// <summary>Submits an async ES|QL query synchronously.</summary>
	IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request);

	/// <summary>Submits an async ES|QL query asynchronously.</summary>
	Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken);

	/// <summary>
	/// Polls the state of an async query synchronously.
	/// <paramref name="request"/> carries the submission-time options and format; its <see cref="EsqlExecutionRequest.Esql"/> is not sent.
	/// </summary>
	IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request);

	/// <summary>
	/// Polls the state of an async query asynchronously.
	/// <paramref name="request"/> carries the submission-time options and format; its <see cref="EsqlExecutionRequest.Esql"/> is not sent.
	/// </summary>
	Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken);

	/// <summary>
	/// Deletes an async query synchronously.
	/// <paramref name="request"/> carries the submission-time options; its <see cref="EsqlExecutionRequest.Esql"/> is not sent.
	/// </summary>
	void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request);

	/// <summary>
	/// Deletes an async query asynchronously.
	/// <paramref name="request"/> carries the submission-time options; its <see cref="EsqlExecutionRequest.Esql"/> is not sent.
	/// </summary>
	Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken);
}
