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

/// <summary>Main entry point for ES|QL queries.</summary>
public class EsqlClient : IDisposable
{
	private readonly EsqlQueryProvider _provider;
	private bool _disposed;

	/// <summary>Gets the client settings.</summary>
	public EsqlClientSettings Settings { get; }

	/// <summary>Creates a new ES|QL client with the specified settings.</summary>
	public EsqlClient(EsqlClientSettings settings)
	{
		Settings = settings ?? throw new ArgumentNullException(nameof(settings));
		var jsonOptions = settings.ResolveJsonOptions();
		var executor = new EsqlTransportExecutor(settings);
		_provider = new EsqlQueryProvider(jsonOptions, executor);
	}

	/// <summary>Creates a new ES|QL client with the specified node URI.</summary>
	public EsqlClient(Uri nodeUri)
		: this(new EsqlClientSettings(nodeUri)) { }

	/// <summary>Creates a new ES|QL client with a connection pool.</summary>
	public EsqlClient(NodePool nodePool)
		: this(new EsqlClientSettings(nodePool)) { }

	/// <summary>Creates a strongly-typed queryable for the specified type.</summary>
	public IEsqlQueryable<T> Query<T>() where T : class => new EsqlQueryable<T>(_provider);

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
	public Task<EsqlAsyncQuery<T>> QueryAsyncQuery<T>(
		Func<IQueryable<T>, IQueryable<T>> query,
		EsqlAsyncQueryOptions? options = null,
		CancellationToken cancellationToken = default) where T : class
	{
		var configured = query(Query<T>());

		if (configured is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.ToAsyncQueryAsync(options, cancellationToken);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>Starts an async ES|QL query using LINQ expression synchronously. Returns IDisposable that auto-deletes.</summary>
	public EsqlAsyncQuery<T> QueryAsyncQuery<T>(
		Func<IQueryable<T>, IQueryable<T>> query,
		EsqlAsyncQueryOptions? options = null) where T : class
	{
		var configured = query(Query<T>());

		if (configured is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.ToAsyncQuery(options);

		throw new InvalidOperationException("Query must return an IEsqlQueryable");
	}

	/// <summary>Executes a standalone ROW + COMPLETION query for LLM inference.</summary>
	public IAsyncEnumerable<T> CompletionAsync<T>(
		string prompt,
		string inferenceId,
		string? column = null,
		CancellationToken cancellationToken = default) where T : class =>
		// TODO: Raw string query execution API
		throw new NotImplementedException("Raw string query execution API is pending redesign.");

	// ================================================================
	// Synchronous API
	// ================================================================

	/// <summary>Executes an ES|QL query using LINQ expression synchronously.</summary>
	public List<T> Query<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class =>
		query(Query<T>()).ToList();

	/// <summary>Executes an ES|QL query with projection using LINQ expression synchronously.</summary>
	public List<TResult> Query<T, TResult>(Func<IQueryable<T>, IQueryable<TResult>> query) where T : class =>
		query(Query<T>()).ToList();

	/// <summary>Executes a standalone ROW + COMPLETION query synchronously for LLM inference.</summary>
	public List<T> Completion<T>(
		string prompt,
		string inferenceId,
		string? column = null) where T : class =>
		// TODO: Raw string query execution API
		throw new NotImplementedException("Raw string query execution API is pending redesign.");

	// TODO: Raw string query methods (Query<T>(string esql), QueryAsync<T>(string esql), QueryAsyncQuery<T>(string esql))
	// are pending API redesign since the reader is now internal to EsqlQueryProvider.

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		if (Settings.Transport is IDisposable disposableTransport)
			disposableTransport.Dispose();
	}
}
