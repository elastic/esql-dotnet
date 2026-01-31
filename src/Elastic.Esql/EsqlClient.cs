// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.TypeMapping;
using Elastic.Transport;

namespace Elastic.Esql;

/// <summary>
/// Main entry point for ES|QL queries.
/// </summary>
/// <remarks>
/// Creates a new ES|QL client with the specified settings.
/// </remarks>
public class EsqlClient(EsqlClientSettings settings) : IDisposable
{
	private readonly EsqlExecutor _executor = new(settings);
	private bool _disposed;

	/// <summary>
	/// Creates a new ES|QL client with the specified node URI.
	/// </summary>
	public EsqlClient(Uri nodeUri)
		: this(new EsqlClientSettings(nodeUri))
	{
	}

	/// <summary>
	/// Creates a new ES|QL client with a connection pool.
	/// </summary>
	public EsqlClient(NodePool nodePool)
		: this(new EsqlClientSettings(nodePool))
	{
	}

	/// <summary>
	/// Gets the client settings.
	/// </summary>
	public EsqlClientSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

	/// <summary>
	/// Creates a strongly-typed queryable for the specified type.
	/// </summary>
	/// <typeparam name="T">The document type. Should have EsqlIndex attribute or will use type name as index.</typeparam>
	public IEsqlQueryable<T> Query<T>() where T : class
	{
		var context = new EsqlQueryContext(Settings);
		var provider = new EsqlQueryProvider(context);
		return new EsqlQueryable<T>(provider);
	}

	/// <summary>
	/// Creates a strongly-typed queryable for the specified index pattern.
	/// </summary>
	public IEsqlQueryable<T> Query<T>(string indexPattern) where T : class
	{
		var context = new EsqlQueryContext(Settings) { IndexPattern = indexPattern };
		var provider = new EsqlQueryProvider(context);
		return new EsqlQueryable<T>(provider);
	}

	/// <summary>
	/// Executes a raw ES|QL query and maps results to a type.
	/// </summary>
	public async Task<List<T>> QueryAsync<T>(string esql, CancellationToken cancellationToken = default)
	{
		var response = await _executor.ExecuteAsync(esql, cancellationToken);
		var materializer = new ResultMaterializer();

		var query = new QueryModel.EsqlQuery { ElementType = typeof(T) };
		return materializer.Materialize<T>(response, query).ToList();
	}

	/// <summary>
	/// Executes a raw ES|QL query with a request object.
	/// </summary>
	public async Task<EsqlResponse> QueryAsync(EsqlRequest request, CancellationToken cancellationToken = default) => await _executor.ExecuteAsync(request, cancellationToken);

	/// <summary>
	/// Gets the status of an async query.
	/// </summary>
	public async Task<EsqlResponse> GetAsyncQueryStatusAsync(string queryId, CancellationToken cancellationToken = default) => await _executor.GetAsyncStatusAsync(queryId, cancellationToken);

	/// <summary>
	/// Deletes an async query.
	/// </summary>
	public async Task DeleteAsyncQueryAsync(string queryId, CancellationToken cancellationToken = default) => await _executor.DeleteAsyncQueryAsync(queryId, cancellationToken);

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
			// Dispose managed resources
			if (Settings.Transport is IDisposable disposableTransport)
				disposableTransport.Dispose();
		}

		_disposed = true;
	}
}
