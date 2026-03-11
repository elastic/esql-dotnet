// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Appends one or more raw ES|QL pipeline fragments to the current query.
	/// </summary>
	public static IQueryable<TSource> RawEsql<TSource>(this IQueryable<TSource> source, string esql)
	{
		Verify.NotNull(source);
		Verify.NotNullOrEmpty(esql);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, string, IQueryable<TSource>>(RawEsql).Method,
			Expression.Constant(esql)
		);
	}

	/// <summary>
	/// Appends one or more raw ES|QL pipeline fragments and changes the downstream result type.
	/// </summary>
	public static IQueryable<TNext> RawEsql<TSource, TNext>(this IQueryable<TSource> source, string esql)
	{
		Verify.NotNull(source);
		Verify.NotNullOrEmpty(esql);

		return CreateQuery<TSource, TNext>(source,
			new Func<IQueryable<TSource>, string, IQueryable<TNext>>(RawEsql<TSource, TNext>).Method,
			Expression.Constant(esql)
		);
	}
}
