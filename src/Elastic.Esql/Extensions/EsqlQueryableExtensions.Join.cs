// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
#if !NET10_0_OR_GREATER
	/// <summary>
	/// Correlates the elements of two sequences based on matching keys (LOOKUP JOIN).
	/// On .NET 10+, use the built-in <c>Queryable.LeftJoin</c> instead.
	/// </summary>
	public static IQueryable<TResult> LeftJoin<TOuter, TInner, TKey, TResult>(
		this IQueryable<TOuter> outer,
		IEnumerable<TInner> inner,
		Expression<Func<TOuter, TKey>> outerKeySelector,
		Expression<Func<TInner, TKey>> innerKeySelector,
		Expression<Func<TOuter, TInner?, TResult>> resultSelector)
	{
		Verify.NotNull(outer);
		Verify.NotNull(inner);
		Verify.NotNull(outerKeySelector);
		Verify.NotNull(innerKeySelector);
		Verify.NotNull(resultSelector);

		return CreateQuery<TOuter, TResult>(outer,
			new Func<IQueryable<TOuter>, IEnumerable<TInner>, Expression<Func<TOuter, TKey>>, Expression<Func<TInner, TKey>>,
				Expression<Func<TOuter, TInner?, TResult>>, IQueryable<TResult>>(LeftJoin).Method,
			GetSourceExpression(inner),
			Expression.Quote(outerKeySelector),
			Expression.Quote(innerKeySelector),
			Expression.Quote(resultSelector)
		);
	}
#endif

	/// <summary>
	/// Correlates the elements of two sequences based on a predicate (LOOKUP JOIN with expression-based ON condition).
	/// </summary>
	public static IQueryable<TResult> LeftJoin<TOuter, TInner, TResult>(
		this IQueryable<TOuter> outer,
		IEnumerable<TInner> inner,
		Expression<Func<TOuter, TInner, bool>> onCondition,
		Expression<Func<TOuter, TInner?, TResult>> resultSelector)
	{
		Verify.NotNull(outer);
		Verify.NotNull(inner);
		Verify.NotNull(onCondition);
		Verify.NotNull(resultSelector);

		return CreateQuery<TOuter, TResult>(outer,
			new Func<IQueryable<TOuter>, IEnumerable<TInner>, Expression<Func<TOuter, TInner, bool>>,
				Expression<Func<TOuter, TInner?, TResult>>, IQueryable<TResult>>(LeftJoin).Method,
			GetSourceExpression(inner),
			Expression.Quote(onCondition),
			Expression.Quote(resultSelector)
		);
	}

	/// <summary>
	/// Performs a LOOKUP JOIN using key selectors and a string index name.
	/// </summary>
	public static IQueryable<TResult> LookupJoin<TOuter, TInner, TKey, TResult>(
		this IQueryable<TOuter> outer,
		string lookupIndex,
		Expression<Func<TOuter, TKey>> outerKeySelector,
		Expression<Func<TInner, TKey>> innerKeySelector,
		Expression<Func<TOuter, TInner?, TResult>> resultSelector)
	{
		Verify.NotNull(outer);
		Verify.NotNullOrEmpty(lookupIndex);
		Verify.NotNull(outerKeySelector);
		Verify.NotNull(innerKeySelector);
		Verify.NotNull(resultSelector);

		return CreateQuery<TOuter, TResult>(outer,
			new Func<IQueryable<TOuter>, string, Expression<Func<TOuter, TKey>>, Expression<Func<TInner, TKey>>,
				Expression<Func<TOuter, TInner?, TResult>>, IQueryable<TResult>>(LookupJoin).Method,
			Expression.Constant(lookupIndex),
			Expression.Quote(outerKeySelector),
			Expression.Quote(innerKeySelector),
			Expression.Quote(resultSelector)
		);
	}

	/// <summary>
	/// Performs a LOOKUP JOIN using a predicate and a string index name.
	/// </summary>
	public static IQueryable<TResult> LookupJoin<TOuter, TInner, TResult>(
		this IQueryable<TOuter> outer,
		string lookupIndex,
		Expression<Func<TOuter, TInner, bool>> onCondition,
		Expression<Func<TOuter, TInner?, TResult>> resultSelector)
	{
		Verify.NotNull(outer);
		Verify.NotNullOrEmpty(lookupIndex);
		Verify.NotNull(onCondition);
		Verify.NotNull(resultSelector);

		return CreateQuery<TOuter, TResult>(outer,
			new Func<IQueryable<TOuter>, string, Expression<Func<TOuter, TInner, bool>>,
				Expression<Func<TOuter, TInner?, TResult>>, IQueryable<TResult>>(LookupJoin).Method,
			Expression.Constant(lookupIndex),
			Expression.Quote(onCondition),
			Expression.Quote(resultSelector)
		);
	}

	private static Expression GetSourceExpression<TSource>(IEnumerable<TSource> source) =>
		source is IQueryable<TSource> q ? q.Expression : Expression.Constant(source, typeof(IEnumerable<TSource>));
}
