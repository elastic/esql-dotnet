// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

namespace Elastic.Esql.Tests.Translation;

internal sealed record TestQueryOptions(string? TimeZone = null, string? Locale = null);

internal static class TestQueryableExtensions
{
	public static IEsqlQueryable<T> WithOptions<T>(this IEsqlQueryable<T> source, TestQueryOptions options)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);

		var method = new Func<IEsqlQueryable<T>, TestQueryOptions, IEsqlQueryable<T>>(WithOptions).Method;
		return (IEsqlQueryable<T>)source.Provider.CreateQuery<T>(
			Expression.Call(null, method, source.Expression, Expression.Constant(options))
		);
	}
}
