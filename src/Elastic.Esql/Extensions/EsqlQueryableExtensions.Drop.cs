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
	/// Specifies fields to drop from the result (DROP command).
	/// </summary>
	public static IQueryable<TSource> Drop<TSource>(this IQueryable<TSource> source, params string[] fields)
	{
		Verify.NotNull(source);
		Verify.NotNull(fields);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, string[], IQueryable<TSource>>(Drop).Method,
			Expression.Constant(fields)
		);
	}

	/// <summary>
	/// Specifies fields to drop from the result (DROP command).
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Target array type is statically referenced in the expression tree.")]
	public static IQueryable<TSource> Drop<TSource>(this IQueryable<TSource> source, params Expression<Func<TSource, object?>>[] fieldSelectors)
	{
		Verify.NotNull(source);
		Verify.NotNull(fieldSelectors);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<TSource, object?>>[], IQueryable<TSource>>(Drop).Method,
			Expression.NewArrayInit(typeof(Expression<Func<TSource, object?>>), fieldSelectors.Select(Expression.Quote))
		);
	}
}
