// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Esql.Execution;
using Elastic.Esql;
using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.Extensions;
using Elastic.Transport;

namespace Elastic.Clients.Esql;

/// <summary>
/// Provides a client for executing ES|QL queries against an Elasticsearch cluster using LINQ expressions.
/// </summary>
/// <remarks>
/// The <see cref="EsqlClient"/> enables strongly-typed, LINQ-based querying of Elasticsearch using the ES|QL language.
/// It supports both synchronous and asynchronous query execution, as well as streaming and long-running async queries.
/// The client manages connection settings and pooling, and is intended to be reused for multiple queries. This type is
/// thread-safe for concurrent use across multiple threads. Dispose the client when it is no longer needed to release
/// any underlying resources.
/// </remarks>
public class EsqlClient : IDisposable
{
	private readonly EsqlQueryProvider _provider;
	private bool _disposed;

	/// <summary>
	/// Gets the client settings.
	/// </summary>
	public EsqlClientSettings Settings { get; }

	/// <summary>
	/// Creates a new ES|QL client with the specified settings.
	/// </summary>
	/// <param name="settings">The <see cref="EsqlClientSettings"/> to use.</param>
	public EsqlClient(EsqlClientSettings settings)
	{
		Settings = settings ?? throw new ArgumentNullException(nameof(settings));

		var jsonOptions = settings.ResolveJsonOptions();
		var executor = new EsqlTransportExecutor(settings);

		_provider = new EsqlQueryProvider(jsonOptions, executor);
	}

	/// <summary>
	/// Creates a new ES|QL client with the specified node URI.
	/// </summary>
	/// <param name="nodeUri">The node URI to use.</param>
	public EsqlClient(Uri nodeUri)
		: this(new EsqlClientSettings(nodeUri)) { }

	/// <summary>
	/// Creates a new ES|QL client with a connection pool.
	/// </summary>
	/// <param name="nodePool">The connection pool to use.</param>
	public EsqlClient(NodePool nodePool)
		: this(new EsqlClientSettings(nodePool)) { }

	/// <summary>
	/// Creates a new queryable object for the specified entity type.
	/// </summary>
	/// <typeparam name="T">The type of the entity to be queried.</typeparam>
	/// <returns>
	/// An <see cref="IEsqlQueryable{T}"/> instance that can be used to build and execute queries for entities of type <typeparamref name="T"/>.
	/// </returns>
	public IEsqlQueryable<T> CreateQuery<T>() where T : class => new EsqlQueryable<T>(_provider);

	/// <summary>
	/// Executes a query against the underlying data source using the specified query.
	/// </summary>
	/// <remarks>
	/// The query is not executed until the returned enumerable is iterated. Multiple enumerations may result in multiple executions against the data source,
	/// depending on the underlying implementation.
	/// </remarks>
	/// <typeparam name="T">The type of the entities to query.</typeparam>
	/// <param name="query">A function that returns the <see cref="IQueryable{T}"/> representing the query to execute.</param>
	/// <returns>An <see cref="IEnumerable{T}"/> containing the results of the executed query.</returns>
	public IEnumerable<T> Query<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class =>
		query(CreateQuery<T>());

	/// <summary>
	/// Executes a query against the underlying data source using the specified query.
	/// </summary>
	/// <remarks>
	/// The query is not executed until the returned enumerable is iterated. Multiple enumerations may result in multiple executions against the data source,
	/// depending on the underlying implementation.
	/// </remarks>
	/// <typeparam name="T">The type of the elements in the data source to query.</typeparam>
	/// <typeparam name="TResult">The type of the elements returned by the query.</typeparam>
	/// <param name="query">A function that returns the <see cref="IQueryable{T}"/> representing the query to execute.</param>
	/// <returns>An <see cref="IEnumerable{TResult}"/> containing the results of the executed query.</returns>
	public IEnumerable<TResult> Query<T, TResult>(Func<IQueryable<T>, IQueryable<TResult>> query) where T : class =>
		query(CreateQuery<T>());

	/// <summary>
	/// Asynchronously executes a query against the underlying data source using the specified query.
	/// </summary>
	/// <remarks>
	/// The query is not executed until the returned enumerable is iterated. Multiple enumerations may result in multiple executions against the data source,
	/// depending on the underlying implementation.
	/// </remarks>
	/// <typeparam name="T">The type of the entities to query.</typeparam>
	/// <param name="query">A function that returns the <see cref="IQueryable{T}"/> representing the query to execute.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous query operation.</param>
	/// <returns>An asynchronous stream of elements of type <typeparamref name="T"/> that match the specified query.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the query function does not return an <see cref="IEsqlQueryable{T}"/> instance.</exception>
	public IAsyncEnumerable<T> QueryAsync<T>(
		Func<IQueryable<T>, IQueryable<T>> query,
		CancellationToken cancellationToken = default) where T : class
	{
		var configured = query(CreateQuery<T>());

		if (configured is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.AsAsyncEnumerable(cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>
	/// Asynchronously executes a query against the underlying data source using the specified query.
	/// </summary>
	/// <remarks>
	/// The query is not executed until the returned enumerable is iterated. Multiple enumerations may result in multiple executions against the data source,
	/// depending on the underlying implementation.
	/// </remarks>
	/// <typeparam name="T">The type of the elements in the data source to query.</typeparam>
	/// <typeparam name="TResult">The type of the elements returned by the query.</typeparam>
	/// <param name="query">A function that returns the <see cref="IQueryable{T}"/> representing the query to execute.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous query operation.</param>
	/// <returns>An asynchronous stream of elements of type <typeparamref name="TResult"/> that match the specified query.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the query function does not return an <see cref="IEsqlQueryable{TResult}"/> instance.</exception>
	public IAsyncEnumerable<TResult> QueryAsync<T, TResult>(
		Func<IQueryable<T>, IQueryable<TResult>> query,
		CancellationToken cancellationToken = default) where T : class
	{
		var configured = query(CreateQuery<T>());

		if (configured is IEsqlQueryable<TResult> esqlQueryable)
			return esqlQueryable.AsAsyncEnumerable(cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>
	/// Submits a server-side asynchronous query based on the specified query and options.
	/// </summary>
	/// <remarks>
	///	Disposing the returned <see cref="EsqlAsyncQuery{T}"/> will automatically cancel the underlying server-side query and release resources.
	/// </remarks>
	/// <typeparam name="T">The type of the entities to query.</typeparam>
	/// <param name="query">A function that returns the <see cref="IQueryable{T}"/> representing the query to execute.</param>
	/// <param name="options">Optional settings that control the behavior of the asynchronous query. May be <see langword="null"/> to use default options.</param>
	/// <returns>An <see cref="EsqlAsyncQuery{T}"/> representing the configured asynchronous query.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the configured query does not return an <see cref="IEsqlQueryable{T}"/> instance.</exception>
	public EsqlAsyncQuery<T> SubmitAsyncQuery<T>(
		Func<IQueryable<T>, IQueryable<T>> query,
		EsqlAsyncQueryOptions? options = null) where T : class
	{
		var configured = query(CreateQuery<T>());

		if (configured is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.ToAsyncQuery(options);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>
	/// Submits a server-side asynchronous query based on the specified query and options.
	/// </summary>
	/// <remarks>
	///	Disposing the returned <see cref="EsqlAsyncQuery{T}"/> will automatically cancel the underlying server-side query and release resources.
	/// </remarks>
	/// <typeparam name="T">The type of the elements in the data source to query.</typeparam>
	/// <typeparam name="TResult">The type of the elements returned by the query.</typeparam>
	/// <param name="query">A function that returns the <see cref="IQueryable{TResult}"/> representing the query to execute.</param>
	/// <param name="options">Optional settings that control the behavior of the asynchronous query. May be <see langword="null"/> to use default options.</param>
	/// <returns>An <see cref="EsqlAsyncQuery{T}"/> representing the configured asynchronous query.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the configured query does not return an <see cref="IEsqlQueryable{TResult}"/> instance.</exception>
	public EsqlAsyncQuery<TResult> SubmitAsyncQuery<T, TResult>(
		Func<IQueryable<T>, IQueryable<TResult>> query,
		EsqlAsyncQueryOptions? options = null) where T : class
	{
		var configured = query(CreateQuery<T>());

		if (configured is IEsqlQueryable<TResult> esqlQueryable)
			return esqlQueryable.ToAsyncQuery(options);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>
	/// Asynchronously submits a server-side asynchronous query based on the specified query and options.
	/// </summary>
	/// <remarks>
	///	Disposing the returned <see cref="EsqlAsyncQuery{T}"/> will automatically cancel the underlying server-side query and release resources.
	/// </remarks>
	/// <typeparam name="T">The type of the entities to query.</typeparam>
	/// <param name="query">A function that returns the <see cref="IQueryable{T}"/> representing the query to execute.</param>
	/// <param name="options">Optional settings that control the behavior of the asynchronous query. May be <see langword="null"/> to use default options.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous query operation.</param>
	/// <returns>An <see cref="EsqlAsyncQuery{T}"/> representing the configured asynchronous query.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the configured query does not return an <see cref="IEsqlQueryable{T}"/> instance.</exception>
	public Task<EsqlAsyncQuery<T>> SubmitAsyncQueryAsync<T>(
		Func<IQueryable<T>, IQueryable<T>> query,
		EsqlAsyncQueryOptions? options = null,
		CancellationToken cancellationToken = default) where T : class
	{
		var configured = query(CreateQuery<T>());

		if (configured is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.ToAsyncQueryAsync(options, cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>
	/// Asynchronously submits a server-side asynchronous query based on the specified query and options.
	/// </summary>
	/// <remarks>
	///	Disposing the returned <see cref="EsqlAsyncQuery{T}"/> will automatically cancel the underlying server-side query and release resources.
	/// </remarks>
	/// <typeparam name="T">The type of the elements in the data source to query.</typeparam>
	/// <typeparam name="TResult">The type of the elements returned by the query.</typeparam>
	/// <param name="query">A function that returns the <see cref="IQueryable{TResult}"/> representing the query to execute.</param>
	/// <param name="options">Optional settings that control the behavior of the asynchronous query. May be <see langword="null"/> to use default options.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous query operation.</param>
	/// <returns>An <see cref="EsqlAsyncQuery{T}"/> representing the configured asynchronous query.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the configured query does not return an <see cref="IEsqlQueryable{TResult}"/> instance.</exception>
	public Task<EsqlAsyncQuery<TResult>> SubmitAsyncQueryAsync<T, TResult>(
		Func<IQueryable<T>, IQueryable<TResult>> query,
		EsqlAsyncQueryOptions? options = null,
		CancellationToken cancellationToken = default) where T : class
	{
		var configured = query(CreateQuery<T>());

		if (configured is IEsqlQueryable<TResult> esqlQueryable)
			return esqlQueryable.ToAsyncQueryAsync(options, cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		if (Settings.Transport is IDisposable disposableTransport)
			disposableTransport.Dispose();
	}
}
