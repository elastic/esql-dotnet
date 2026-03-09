// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>Submits the query as an async ES|QL query synchronously and returns an <see cref="EsqlAsyncQuery{T}"/>.</summary>
	public static EsqlAsyncQuery<TSource> ToAsyncQuery<TSource>(
		this IEsqlQueryable<TSource> source,
		EsqlAsyncQueryOptions? options = null)
	{
		Verify.NotNull(source);

		if (source.Provider is not EsqlQueryProvider provider)
			throw new NotSupportedException("This method is only valid for EsqlQueryable.");

		return provider.SubmitAsyncQuery<TSource>(source.Expression, options);
	}

	/// <summary>Submits the query as an async ES|QL query asynchronously and returns an <see cref="EsqlAsyncQuery{T}"/>.</summary>
	public static Task<EsqlAsyncQuery<TSource>> ToAsyncQueryAsync<TSource>(
		this IEsqlQueryable<TSource> source,
		EsqlAsyncQueryOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		if (source.Provider is not EsqlQueryProvider provider)
			throw new NotSupportedException("This method is only valid for EsqlQueryable.");

		return provider.SubmitAsyncQueryAsync<TSource>(source.Expression, options, cancellationToken);
	}
}
