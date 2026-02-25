// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Produces a row with one or more columns with values that you specify (ROW command).
	/// </summary>
	public static IQueryable<TSource> Row<TSource>(this IQueryable<TSource> source, Expression<Func<object>> columns)
	{
		Verify.NotNull(source);
		Verify.NotNull(columns);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<object>>, IQueryable<TSource>>(Row).Method,
			Expression.Quote(columns)
		);
	}

	/// <summary>
	/// Specifies the index pattern to use for the query.
	/// </summary>
	public static IQueryable<TSource> From<TSource>(this IQueryable<TSource> source, string indexPattern)
	{
		Verify.NotNull(source);
		Verify.NotNull(indexPattern);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, string, IQueryable<TSource>>(From).Method,
			Expression.Constant(indexPattern)
		);
	}
}
