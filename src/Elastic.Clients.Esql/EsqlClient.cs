// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Runtime.CompilerServices;
using System.Text.Json;
using Elastic.Clients.Esql.Execution;
using Elastic.Esql;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.Materialization;
using Elastic.Transport;

namespace Elastic.Clients.Esql;

/// <summary>Main entry point for ES|QL queries.</summary>
public class EsqlClient : IDisposable
{
	private readonly EsqlTransportExecutor _executor;
	private readonly JsonSerializerOptions _jsonOptions;
	private readonly EsqlClientQueryProvider _provider;
	private bool _disposed;

	/// <summary>Gets the client settings.</summary>
	public EsqlClientSettings Settings { get; }

	/// <summary>Creates a new ES|QL client with the specified settings.</summary>
	public EsqlClient(EsqlClientSettings settings)
	{
		Settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_jsonOptions = settings.ResolveJsonOptions();
		_executor = new EsqlTransportExecutor(settings);
		_provider = new EsqlClientQueryProvider(_executor, _jsonOptions);
	}

	/// <summary>Creates a new ES|QL client with the specified node URI.</summary>
	public EsqlClient(Uri nodeUri)
		: this(new EsqlClientSettings(nodeUri))
	{
	}

	/// <summary>Creates a new ES|QL client with a connection pool.</summary>
	public EsqlClient(NodePool nodePool)
		: this(new EsqlClientSettings(nodePool))
	{
	}

	/// <summary>Creates a strongly-typed queryable for the specified type.</summary>
	/// <typeparam name="T">The document type.</typeparam>
	public IEsqlQueryable<T> Query<T>() where T : class => new EsqlQueryable<T>(_provider);

	/// <summary>Executes a raw ES|QL query and streams results as they are parsed.</summary>
	public IAsyncEnumerable<T> QueryAsync<T>(string esql, CancellationToken cancellationToken = default) =>
		StreamResultsAsync<T>(esql, null, cancellationToken);

	/// <summary>Executes a raw ES|QL query with options and streams results as they are parsed.</summary>
	public IAsyncEnumerable<T> QueryAsync<T>(string esql, EsqlQueryOptions options, CancellationToken cancellationToken = default) =>
		StreamResultsAsync<T>(esql, options, cancellationToken);

	/// <summary>Executes a raw ES|QL query with a request object.</summary>
	public async Task<EsqlResponse> QueryAsync(EsqlRequest request, CancellationToken cancellationToken = default) =>
		await _executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

	/// <summary>Executes an ES|QL query using LINQ expression and streams results.</summary>
	public IAsyncEnumerable<T> QueryAsync<T>(
		Func<IQueryable<T>, IQueryable<T>> query,
		CancellationToken cancellationToken = default) where T : class
	{
		var configured = query(Query<T>());

		if (configured is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.AsAsyncEnumerable(cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>Executes an ES|QL query with projection using LINQ expression and streams results.</summary>
	public IAsyncEnumerable<TResult> QueryAsync<T, TResult>(
		Func<IQueryable<T>, IQueryable<TResult>> query,
		CancellationToken cancellationToken = default) where T : class
	{
		var configured = query(Query<T>());

		if (configured is IEsqlQueryable<TResult> esqlQueryable)
			return esqlQueryable.AsAsyncEnumerable(cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>Starts an async ES|QL query using LINQ expression. Returns IAsyncDisposable that auto-deletes.</summary>
	public async Task<EsqlAsyncQuery<T>> QueryAsyncQuery<T>(
		Func<IQueryable<T>, IQueryable<T>> query,
		EsqlAsyncQueryOptions? options = null,
		CancellationToken cancellationToken = default) where T : class
	{
		var queryable = Query<T>();
		var configured = query(queryable);

		if (configured is IEsqlQueryable<T> esqlQueryable)
		{
			var esql = esqlQueryable.ToEsqlString();
			var request = BuildAsyncRequest(esql, null, options);
			return await _executor.ExecuteAsyncAsync<T>(request, cancellationToken);
		}

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>Starts an async ES|QL query from a raw query string. Returns IAsyncDisposable that auto-deletes.</summary>
	public async Task<EsqlAsyncQuery<T>> QueryAsyncQuery<T>(
		string esql,
		EsqlAsyncQueryOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		var request = BuildAsyncRequest(esql, null, options);
		return await _executor.ExecuteAsyncAsync<T>(request, cancellationToken);
	}

	/// <summary>Executes a standalone ROW + COMPLETION query for LLM inference.</summary>
	public IAsyncEnumerable<T> CompletionAsync<T>(
		string prompt,
		string inferenceId,
		string? column = null,
		CancellationToken cancellationToken = default) where T : class
	{
		var esql = Query<T>()
			.Row(() => new { prompt })
			.Completion("prompt", inferenceId, column)
			.ToEsqlString();
		return QueryAsync<T>(esql, cancellationToken);
	}

	/// <summary>Gets the status of an async query.</summary>
	public async Task<EsqlResponse> GetAsyncQueryStatusAsync(string queryId, CancellationToken cancellationToken = default) =>
		await _executor.GetAsyncStatusAsync(queryId, cancellationToken);

	/// <summary>Deletes an async query.</summary>
	public async Task DeleteAsyncQueryAsync(string queryId, CancellationToken cancellationToken = default) =>
		await _executor.DeleteAsyncQueryAsync(queryId, cancellationToken);

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
			return;

		if (disposing)
		{
			if (Settings.Transport is IDisposable disposableTransport)
				disposableTransport.Dispose();
		}

		_disposed = true;
	}

	private async IAsyncEnumerable<T> StreamResultsAsync<T>(
		string esql,
		EsqlQueryOptions? options,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
#if NET10_0_OR_GREATER
		await using var response = await _executor.ExecuteStreamingAsync(esql, options, cancellationToken).ConfigureAwait(false);

		await foreach (var item in EsqlResponseReader.ReadRowsAsync<T>(response.Body, _jsonOptions, cancellationToken)
			.ConfigureAwait(false))
			yield return item;
#else
		using var response = await _executor.ExecuteStreamingAsync(esql, options, cancellationToken).ConfigureAwait(false);

		await foreach (var item in EsqlResponseReader.ReadRowsAsync<T>(response.Body, _jsonOptions, cancellationToken)
			.ConfigureAwait(false))
			yield return item;
#endif
	}

	private EsqlAsyncRequest BuildAsyncRequest(string esql, EsqlQueryOptions? queryOptions, EsqlAsyncQueryOptions? asyncOptions)
	{
		var defaults = Settings.Defaults;
		return new EsqlAsyncRequest
		{
			Query = esql,
			Columnar = queryOptions?.Columnar ?? defaults.Columnar,
			Profile = queryOptions?.IncludeProfile ?? defaults.IncludeProfile,
			Locale = queryOptions?.Locale ?? defaults.Locale,
			TimeZone = queryOptions?.TimeZone ?? defaults.TimeZone,
			Params = queryOptions?.Parameters?.ToList(),
			WaitForCompletionTimeout = asyncOptions?.WaitForCompletionTimeout,
			KeepAlive = asyncOptions?.KeepAlive,
			KeepOnCompletion = asyncOptions?.KeepOnCompletion ?? false
		};
	}
}
