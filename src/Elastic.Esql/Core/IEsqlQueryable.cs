// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Core;

/// <summary>
/// Extended <see cref="IQueryable{T}"/> interface for ES|QL queries.
/// </summary>
public interface IEsqlQueryable<out T> : IQueryable<T>
{
	/// <summary>
	/// Translates the query and returns the ES|QL query string.
	/// </summary>
	/// <param name="inlineParameters">Set <see langword="true"/> to inline captured variables instead of translating them to <c>?name</c> placeholders.</param>
	/// <returns>The ES|QL query string.</returns>
	string ToEsqlString(bool inlineParameters = true);

	/// <summary>
	/// Translates the query and returns the collected named parameters, or <see langword="null"/> if none.
	/// </summary>
	/// <returns>An <see cref="EsqlParameters"/> object containing the collected parameters for the query, or <see langword="null"/> if none.</returns>
	EsqlParameters? GetParameters();

	/// <summary>
	/// Returns an async enumerable for streaming query results.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous iteration.</param>
	/// <returns>An asynchronous enumerable that yields elements of type <typeparamref name="T"/> as they become available.</returns>
	IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default);
}
