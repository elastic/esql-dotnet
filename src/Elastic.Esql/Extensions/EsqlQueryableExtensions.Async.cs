// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Core;
using Elastic.Esql.QueryModel;
using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

/// <summary>
/// Extension methods for ES|QL queryables.
/// </summary>
public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Executes the query and returns results as a list.
	/// </summary>
	public static async Task<List<TSource>> ToListAsync<TSource>(this IEsqlQueryable<TSource> source, CancellationToken cancellationToken = default)
	{
		var list = new List<TSource>();

		await foreach (var item in source.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			list.Add(item);

		return list;
	}

	/// <summary>
	/// Executes the query and returns results as an array.
	/// </summary>
	public static async Task<TSource[]> ToArrayAsync<TSource>(this IEsqlQueryable<TSource> source, CancellationToken cancellationToken = default) =>
		(await source.ToListAsync(cancellationToken)).ToArray();

	/// <summary>
	/// Returns the first element or throws if none exist.
	/// </summary>
	public static Task<TSource> FirstAsync<TSource>(this IEsqlQueryable<TSource> source, CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		return ExecuteAsync<TSource, TSource>(source,
			new Func<IEsqlQueryable<TSource>, CancellationToken, Task<TSource>>(FirstAsync).Method,
			cancellationToken
		);
	}

	/// <summary>
	/// Returns the first element or default if none exist.
	/// </summary>
	public static Task<TSource?> FirstOrDefaultAsync<TSource>(this IEsqlQueryable<TSource> source, CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		return ExecuteAsync<TSource, TSource?>(source,
			new Func<IEsqlQueryable<TSource>, CancellationToken, Task<TSource?>>(FirstOrDefaultAsync).Method,
			cancellationToken
		);
	}

	/// <summary>
	/// Returns the single element or throws.
	/// </summary>
	public static Task<TSource> SingleAsync<TSource>(this IEsqlQueryable<TSource> source, CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		return ExecuteAsync<TSource, TSource>(source,
			new Func<IEsqlQueryable<TSource>, CancellationToken, Task<TSource>>(SingleAsync).Method,
			cancellationToken
		);
	}

	/// <summary>
	/// Returns the single element or default.
	/// </summary>
	public static Task<TSource?> SingleOrDefaultAsync<TSource>(this IEsqlQueryable<TSource> source, CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		return ExecuteAsync<TSource, TSource?>(source,
			new Func<IEsqlQueryable<TSource>, CancellationToken, Task<TSource?>>(SingleOrDefaultAsync).Method,
			cancellationToken
		);
	}

	/// <summary>
	/// Returns the count of elements.
	/// </summary>
	public static Task<int> CountAsync<TSource>(this IEsqlQueryable<TSource> source, CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		return ExecuteAsync<TSource, int>(source,
			new Func<IEsqlQueryable<TSource>, CancellationToken, Task<int>>(CountAsync).Method,
			cancellationToken
		);
	}

	/// <summary>
	/// Returns whether any elements exist.
	/// </summary>
	public static Task<bool> AnyAsync<TSource>(this IEsqlQueryable<TSource> source, CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		return ExecuteAsync<TSource, bool>(source,
			new Func<IEsqlQueryable<TSource>, CancellationToken, Task<bool>>(AnyAsync).Method,
			cancellationToken
		);
	}
}
