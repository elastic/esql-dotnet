// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Core;

/// <summary>
/// Extended IQueryable interface for ES|QL queries.
/// </summary>
public interface IEsqlQueryable<out T> : IQueryable<T>
{
	/// <summary>
	/// Gets the ES|QL query string without executing the query.
	/// </summary>
	string ToEsqlString();

	/// <summary>
	/// Gets the query context.
	/// </summary>
	EsqlQueryContext Context { get; }

	/// <summary>
	/// Returns an async enumerable for streaming query results.
	/// </summary>
	IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default);
}
