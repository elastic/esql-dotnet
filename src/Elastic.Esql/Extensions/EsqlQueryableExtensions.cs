// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Extensions;

/// <summary>
/// Extension methods for ES|QL queryables.
/// </summary>
public static partial class EsqlQueryableExtensions
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

	// TODO: Reimplement properly.
	/// <summary>
	/// Returns the count of elements.
	/// </summary>
	public static async Task<int> CountAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		_ = queryable;
		_ = cancellationToken;
		await Task.CompletedTask.ConfigureAwait(false);

		throw new NotImplementedException();
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
	/// Translates the query and returns the ES|QL query string.
	/// </summary>
	/// <param name="queryable">The queryable.</param>
	/// <param name="inlineParameters">Set <see langword="true"/> to inline captured variables instead of translating them to <c>?name</c> placeholders.</param>
	/// <returns>The ES|QL query string.</returns>
	public static string ToEsqlString<T>(this IQueryable<T> queryable, bool inlineParameters = true)
	{
		if (queryable is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.ToEsqlString(inlineParameters);

		throw new InvalidOperationException("Query is not an ES|QL query.");
	}

	/// <summary>
	/// Translates the query and returns the collected named parameters, or <see langword="null"/> if none.
	/// </summary>
	/// <param name="queryable">The queryable.</param>
	/// <returns>An <see cref="EsqlParameters"/> object containing the collected parameters for the query, or <see langword="null"/> if none.</returns>
	public static EsqlParameters? GetParameters<T>(this IQueryable<T> queryable)
	{
		if (queryable is not IEsqlQueryable<T> esqlQueryable)
			throw new InvalidOperationException("Query is not an ES|QL query.");

		return esqlQueryable.GetParameters();
	}
}
