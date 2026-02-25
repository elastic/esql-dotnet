// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Specifies fields to keep in the result (KEEP command).
	/// </summary>
	public static IQueryable<TSource> Keep<TSource>(this IQueryable<TSource> source, params string[] fields)
	{
		Verify.NotNull(source);
		Verify.NotNull(fields);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, string[], IQueryable<TSource>>(Keep).Method,
			Expression.Constant(fields)
		);
	}

	/// <summary>
	/// Specifies fields to keep using lambda selectors (KEEP command).
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Target array type is statically referenced in the expression tree.")]
	public static IQueryable<TSource> Keep<TSource>(this IQueryable<TSource> source, params Expression<Func<TSource, object?>>[] fieldSelectors)
	{
		Verify.NotNull(source);
		Verify.NotNull(fieldSelectors);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<TSource, object?>>[], IQueryable<TSource>>(Keep).Method,
			Expression.NewArrayInit(typeof(Expression<Func<TSource, object?>>), fieldSelectors.Select(Expression.Quote))
		);
	}

	/// <summary>
	/// Specifies fields to keep with optional aliases via a projection.
	/// </summary>
	public static IQueryable<TSource> Keep<TSource, TResult>(this IQueryable<TSource> source, Expression<Func<TSource, TResult>> projection)
	{
		Verify.NotNull(source);
		Verify.NotNull(projection);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<TSource, TResult>>, IQueryable<TSource>>(Keep).Method,
			Expression.Quote(projection)
		);
	}
}
