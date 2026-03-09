// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Execution;

/// <summary>
/// Abstracts the transport layer for ES|QL query execution.
/// Implementations issue HTTP requests and return raw response bodies
/// as <see cref="IEsqlResponse"/> (sync / <see cref="Stream"/>) or
/// <see cref="IEsqlAsyncResponse"/> (async / <see cref="System.IO.Pipelines.PipeReader"/>).
/// </summary>
public interface IEsqlQueryExecutor
{
	/// <summary>Executes an ES|QL query synchronously.</summary>
	IEsqlResponse ExecuteQuery(string esql, EsqlQueryOptions? options);

	/// <summary>Executes an ES|QL query asynchronously.</summary>
	Task<IEsqlAsyncResponse> ExecuteQueryAsync(string esql, EsqlQueryOptions? options, CancellationToken cancellationToken);

	/// <summary>Submits an async ES|QL query synchronously.</summary>
	IEsqlResponse SubmitAsyncQuery(string esql, EsqlQueryOptions? queryOptions, EsqlAsyncQueryOptions? asyncOptions);

	/// <summary>Submits an async ES|QL query asynchronously.</summary>
	Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(
		string esql,
		EsqlQueryOptions? queryOptions,
		EsqlAsyncQueryOptions? asyncOptions,
		CancellationToken cancellationToken
	);

	/// <summary>Polls the state of an async query synchronously.</summary>
	IEsqlResponse PollAsyncQuery(string queryId);

	/// <summary>Polls the state of an async query asynchronously.</summary>
	Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, CancellationToken cancellationToken);

	/// <summary>Deletes an async query synchronously.</summary>
	void DeleteAsyncQuery(string queryId);

	/// <summary>Deletes an async query asynchronously.</summary>
	Task DeleteAsyncQueryAsync(string queryId, CancellationToken cancellationToken);
}
