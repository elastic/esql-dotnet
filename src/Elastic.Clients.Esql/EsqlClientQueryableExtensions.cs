// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Core;

namespace Elastic.Clients.Esql;

/// <summary>Extension methods for <see cref="IEsqlQueryable{T}"/> specific to the Elasticsearch transport executor.</summary>
public static class EsqlClientQueryableExtensions
{
	/// <summary>Attaches Elasticsearch-specific query options to the query pipeline.</summary>
	public static IEsqlQueryable<T> WithOptions<T>(this IEsqlQueryable<T> source, EsqlQueryOptions options)
	{
#if NETSTANDARD2_0
		if (source is null)
			throw new ArgumentNullException(nameof(source));
		if (options is null)
			throw new ArgumentNullException(nameof(options));
#else
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(options);
#endif

		var method = new Func<IEsqlQueryable<T>, EsqlQueryOptions, IEsqlQueryable<T>>(WithOptions).Method;
		return (IEsqlQueryable<T>)source.Provider.CreateQuery<T>(
			Expression.Call(null, method, source.Expression, Expression.Constant(options))
		);
	}
}
