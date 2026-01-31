// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Extensions;

/// <summary>
/// Extension methods for ES|QL queryables.
/// </summary>
public static class EsqlQueryableExtensions
{
	/// <summary>
	/// Executes the query and returns results as a list.
	/// </summary>
	public static async Task<List<T>> ToListAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var list = new List<T>();
		await foreach (var item in queryable.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			list.Add(item);
		return list;
	}

	/// <summary>
	/// Executes the query and returns results as an array.
	/// </summary>
	public static async Task<T[]> ToArrayAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default) =>
		(await queryable.ToListAsync(cancellationToken)).ToArray();

	/// <summary>
	/// Returns the first element or throws if none exist.
	/// </summary>
	public static async Task<T> FirstAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 1);
		await foreach (var item in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			return item;
		throw new InvalidOperationException("Sequence contains no elements.");
	}

	/// <summary>
	/// Returns the first element or default if none exist.
	/// </summary>
	public static async Task<T?> FirstOrDefaultAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 1);
		await foreach (var item in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			return item;
		return default;
	}

	/// <summary>
	/// Returns the single element or throws.
	/// </summary>
	public static async Task<T> SingleAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		T? result = default;
		var found = false;

		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 2);
		await foreach (var item in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
		{
			if (found)
				throw new InvalidOperationException("Sequence contains more than one element.");
			result = item;
			found = true;
		}

		if (!found)
			throw new InvalidOperationException("Sequence contains no elements.");

		return result!;
	}

	/// <summary>
	/// Returns the single element or default.
	/// </summary>
	public static async Task<T?> SingleOrDefaultAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		T? result = default;
		var found = false;

		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 2);
		await foreach (var item in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
		{
			if (found)
				throw new InvalidOperationException("Sequence contains more than one element.");
			result = item;
			found = true;
		}

		return result;
	}

	/// <summary>
	/// Returns the count of elements.
	/// </summary>
	public static async Task<int> CountAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		// For count, we need to use STATS COUNT(*)
		var esql = queryable.ToEsqlString();

		// Append STATS COUNT(*) to the query
		var countQuery = esql + Environment.NewLine + "| STATS count = COUNT(*)";

		var executor = new EsqlExecutor(queryable.Context.Settings);
		var response = await executor.ExecuteAsync(countQuery, cancellationToken);

		var materializer = new ResultMaterializer();
		return materializer.MaterializeScalar<int>(response);
	}

	/// <summary>
	/// Returns whether any elements exist.
	/// </summary>
	public static async Task<bool> AnyAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 1);
		await foreach (var _ in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			return true;
		return false;
	}

	/// <summary>
	/// Gets the ES|QL query string without executing.
	/// </summary>
	public static string ToEsqlString<T>(this IQueryable<T> queryable)
	{
		if (queryable is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.ToEsqlString();

		throw new InvalidOperationException("Query is not an ES|QL query.");
	}

	/// <summary>
	/// Specifies fields to keep in the result (KEEP command).
	/// </summary>
	public static IEsqlQueryable<T> Keep<T>(
		this IEsqlQueryable<T> queryable,
		params string[] fields)
	{
		var query = ((EsqlQueryProvider)queryable.Provider).TranslateExpression(queryable.Expression);
		query.AddCommand(new KeepCommand(fields));

		// Create a new queryable with the modified query
		// For now, we'll just return the same queryable - the KEEP will be handled in projection
		return queryable;
	}

	/// <summary>
	/// Specifies fields to drop from the result (DROP command).
	/// </summary>
	public static IEsqlQueryable<T> Drop<T>(
		this IEsqlQueryable<T> queryable,
		params string[] fields)
	{
		var query = ((EsqlQueryProvider)queryable.Provider).TranslateExpression(queryable.Expression);
		query.AddCommand(new DropCommand(fields));

		return queryable;
	}

	/// <summary>
	/// Adds a full-text match filter.
	/// </summary>
	public static IQueryable<T> Match<T>(
		this IQueryable<T> queryable,
		string field,
		string query) =>
		// This will be handled by the WhereClauseVisitor when it sees EsqlFunctions.Match
		queryable.Where(_ => Elastic.Esql.Functions.EsqlFunctions.Match(field, query));
}
