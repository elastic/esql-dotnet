// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Microsoft.Extensions.Logging;

namespace Elastic.Integration.Tests.Infrastructure;

/// <summary>Compares ES|QL query results against LINQ to Objects with the same data.</summary>
public static class QueryComparer
{
	/// <summary>
	/// Executes the same query against ES|QL and LINQ, then compares results.
	/// </summary>
	/// <typeparam name="TSource">The source entity type.</typeparam>
	/// <typeparam name="TResult">The projected result type.</typeparam>
	/// <param name="esqlSource">ES|QL queryable source.</param>
	/// <param name="linqSource">In-memory LINQ source with same data.</param>
	/// <param name="queryBuilder">Query function applied to both sources.</param>
	/// <param name="asserter">Custom assertion function for comparing individual results.</param>
	/// <param name="logger">Optional logger for diagnostics.</param>
	public static async Task CompareAsync<TSource, TResult>(
		IQueryable<TSource> esqlSource,
		IQueryable<TSource> linqSource,
		Func<IQueryable<TSource>, IQueryable<TResult>> queryBuilder,
		Action<TResult, TResult> asserter,
		ILogger? logger = null)
	{
		var esqlQuery = queryBuilder(esqlSource);
		var linqQuery = queryBuilder(linqSource);

		var esqlResults = await ToListAsync(esqlQuery);
		var linqResults = linqQuery.ToList();

		logger?.LogInformation("ES|QL: {EsqlCount}, LINQ: {LinqCount}", esqlResults.Count, linqResults.Count);

		esqlResults.Should().HaveCount(linqResults.Count);
		for (var i = 0; i < esqlResults.Count; i++)
			asserter(esqlResults[i], linqResults[i]);
	}

	/// <summary>
	/// Executes the same query against ES|QL and LINQ, comparing result counts only.
	/// </summary>
	public static async Task CompareCountAsync<TSource, TResult>(
		IQueryable<TSource> esqlSource,
		IQueryable<TSource> linqSource,
		Func<IQueryable<TSource>, IQueryable<TResult>> queryBuilder,
		ILogger? logger = null)
	{
		var esqlQuery = queryBuilder(esqlSource);
		var linqQuery = queryBuilder(linqSource);

		var esqlResults = await ToListAsync(esqlQuery);
		var linqResults = linqQuery.ToList();

		logger?.LogInformation("ES|QL: {EsqlCount}, LINQ: {LinqCount}", esqlResults.Count, linqResults.Count);

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	private static async Task<List<T>> ToListAsync<T>(IQueryable<T> query)
	{
		// Check if this is an IAsyncEnumerable (ES|QL queryable)
		if (query is IAsyncEnumerable<T> asyncEnumerable)
		{
			var results = new List<T>();
			await foreach (var item in asyncEnumerable)
				results.Add(item);
			return results;
		}

		// Fall back to sync evaluation
		return query.ToList();
	}
}
