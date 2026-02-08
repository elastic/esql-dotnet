// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.Extensions;
using Elastic.Mapping;
using Elastic.Transport;

namespace Elastic.Esql;

/// <summary>Main entry point for ES|QL queries.</summary>
public class EsqlClient(EsqlClientSettings settings) : IDisposable
{
	private readonly EsqlExecutor _executor = new(settings);
	private bool _disposed;

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

	/// <summary>Creates an in-memory client for string generation only.</summary>
	public static EsqlClient InMemory(IElasticsearchMappingContext? mappingContext = null) =>
		new(EsqlClientSettings.InMemory(mappingContext));

	/// <summary>Gets the client settings.</summary>
	public EsqlClientSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

	/// <summary>Creates a strongly-typed queryable for the specified type.</summary>
	/// <typeparam name="T">The document type. Should have EsqlIndex attribute or will use type name as index.</typeparam>
	public IEsqlQueryable<T> Query<T>() where T : class
	{
		var context = new EsqlQueryContext(Settings);
		var provider = new EsqlQueryProvider(context);
		return new EsqlQueryable<T>(provider);
	}

	/// <summary>Creates a strongly-typed queryable for the specified index pattern.</summary>
	public IEsqlQueryable<T> Query<T>(string indexPattern) where T : class
	{
		var context = new EsqlQueryContext(Settings) { IndexPattern = indexPattern };
		var provider = new EsqlQueryProvider(context);
		return new EsqlQueryable<T>(provider);
	}

	/// <summary>Executes a raw ES|QL query and maps results to a type.</summary>
	public async Task<List<T>> QueryAsync<T>(string esql, CancellationToken cancellationToken = default)
	{
		var response = await _executor.ExecuteAsync(esql, cancellationToken);
		var materializer = new ResultMaterializer(new Mapping.TypeFieldMetadataResolver(Settings.MappingContext));

		var query = new QueryModel.EsqlQuery { ElementType = typeof(T) };
		return materializer.Materialize<T>(response, query).ToList();
	}

	/// <summary>Executes a raw ES|QL query with options and maps results to a type.</summary>
	public async Task<List<T>> QueryAsync<T>(string esql, EsqlQueryOptions options, CancellationToken cancellationToken = default)
	{
		var response = await _executor.ExecuteAsync(esql, options, cancellationToken);
		var materializer = new ResultMaterializer(new Mapping.TypeFieldMetadataResolver(Settings.MappingContext));

		var query = new QueryModel.EsqlQuery { ElementType = typeof(T) };
		return materializer.Materialize<T>(response, query).ToList();
	}

	/// <summary>Executes a raw ES|QL query with a request object.</summary>
	public async Task<EsqlResponse> QueryAsync(EsqlRequest request, CancellationToken cancellationToken = default) =>
		await _executor.ExecuteAsync(request, cancellationToken);

	/// <summary>
	/// Executes an ES|QL query using LINQ expression or query syntax.
	/// </summary>
	/// <example>
	/// // Method syntax
	/// await client.QueryAsync&lt;Log&gt;(q =&gt; q.Where(l =&gt; l.Level == "ERROR").Take(10));
	///
	/// // Query syntax
	/// await client.QueryAsync&lt;Log&gt;(q =&gt;
	///     from log in q
	///     where log.Level == "ERROR"
	///     orderby log.Timestamp descending
	///     select log
	/// );
	/// </example>
	public async Task<List<T>> QueryAsync<T>(
		Func<IQueryable<T>, IQueryable<T>> query,
		CancellationToken cancellationToken = default) where T : class
	{
		var queryable = Query<T>();
		var configured = query(queryable);

		if (configured is IEsqlQueryable<T> esqlQueryable)
			return await esqlQueryable.ToListAsync(cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>
	/// Executes an ES|QL query with projection using LINQ expression.
	/// </summary>
	public async Task<List<TResult>> QueryAsync<T, TResult>(
		Func<IQueryable<T>, IQueryable<TResult>> query,
		CancellationToken cancellationToken = default) where T : class
	{
		var queryable = Query<T>();
		var configured = query(queryable);

		if (configured is IEsqlQueryable<TResult> esqlQueryable)
			return await esqlQueryable.ToListAsync(cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>
	/// Starts an async ES|QL query using LINQ expression. Returns IAsyncDisposable that auto-deletes.
	/// </summary>
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
			var request = BuildAsyncRequest(esql, esqlQueryable.Context.QueryOptions, options);
			return await _executor.ExecuteAsyncAsync<T>(request, cancellationToken);
		}

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>
	/// Starts an async ES|QL query from a raw query string. Returns IAsyncDisposable that auto-deletes.
	/// </summary>
	public async Task<EsqlAsyncQuery<T>> QueryAsyncQuery<T>(
		string esql,
		EsqlAsyncQueryOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		var request = BuildAsyncRequest(esql, null, options);
		return await _executor.ExecuteAsyncAsync<T>(request, cancellationToken);
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
