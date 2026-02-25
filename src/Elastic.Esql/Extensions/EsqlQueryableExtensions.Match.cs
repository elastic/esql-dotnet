// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Adds a full-text match filter.
	/// </summary>
	public static IQueryable<T> Match<T>(this IQueryable<T> source, string field, string query) =>
		source.Where(_ => Functions.EsqlFunctions.Match(field, query));
}
