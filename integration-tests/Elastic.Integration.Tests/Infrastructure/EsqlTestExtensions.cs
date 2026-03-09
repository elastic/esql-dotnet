// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;

namespace Elastic.Esql.Integration.Tests.Infrastructure;

/// <summary>
/// Convenience extension to bridge from IQueryable{T} (returned by LINQ operators)
/// back to IEsqlQueryable{T} (required by async execution methods).
/// </summary>
public static class EsqlTestExtensions
{
	public static IEsqlQueryable<T> AsEsql<T>(this IQueryable<T> source) =>
		source as IEsqlQueryable<T>
		?? throw new InvalidOperationException("Query is not an ES|QL queryable.");
}
