// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Core;
using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>Attaches protocol-level query options to the query pipeline.</summary>
	public static IEsqlQueryable<T> WithOptions<T>(this IEsqlQueryable<T> source, EsqlQueryOptions options)
	{
		Verify.NotNull(source);
		Verify.NotNull(options);

		var method = new Func<IEsqlQueryable<T>, EsqlQueryOptions, IEsqlQueryable<T>>(WithOptions).Method;
		return (IEsqlQueryable<T>)source.Provider.CreateQuery<T>(
			Expression.Call(null, method, source.Expression, Expression.Constant(options))
		);
	}
}
