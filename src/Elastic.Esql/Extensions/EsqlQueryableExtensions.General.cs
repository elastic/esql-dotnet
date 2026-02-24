// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Text;

using Elastic.Esql.Core;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Translates the query and returns the ES|QL query string.
	/// </summary>
	/// <param name="queryable">The queryable.</param>
	/// <param name="inlineParameters">Set <see langword="true"/> to inline captured variables instead of translating them to <c>?name</c> placeholders.</param>
	/// <returns>The ES|QL query string.</returns>
	public static string ToEsqlString<T>(this IQueryable<T> queryable, bool inlineParameters = true)
	{
		if (queryable is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.ToEsqlString(inlineParameters);

		throw new InvalidOperationException("Query is not an ES|QL query.");
	}

	/// <summary>
	/// Translates the query and returns the collected named parameters, or <see langword="null"/> if none.
	/// </summary>
	/// <param name="queryable">The queryable.</param>
	/// <returns>An <see cref="EsqlParameters"/> object containing the collected parameters for the query, or <see langword="null"/> if none.</returns>
	public static EsqlParameters? GetParameters<T>(this IQueryable<T> queryable)
	{
		if (queryable is not IEsqlQueryable<T> esqlQueryable)
			throw new InvalidOperationException("Query is not an ES|QL query.");

		return esqlQueryable.GetParameters();
	}
}
