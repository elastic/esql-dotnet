// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;

namespace Elastic.Esql;

/// <summary>
/// Static entry point for ES|QL string generation without client instantiation.
/// </summary>
public static class Esql
{
	private static readonly EsqlClient InMemoryClient = new(EsqlClientSettings.InMemory());

	/// <summary>
	/// Creates a queryable for ES|QL string generation.
	/// Uses index pattern from [EsqlIndex] attribute on T.
	/// </summary>
	/// <example>
	/// // Fluent syntax
	/// var esql = Esql.From&lt;LogEntry&gt;()
	///     .Where(l => l.Level == "ERROR")
	///     .Take(10)
	///     .ToEsqlString();
	///
	/// // Query syntax
	/// var esql = (
	///     from l in Esql.From&lt;LogEntry&gt;()
	///     where l.Level == "ERROR"
	///     select l
	/// ).ToEsqlString();
	/// </example>
	/// <typeparam name="T">The document type. Should have EsqlIndex attribute or will use type name as index.</typeparam>
	/// <returns>A queryable that can be used to build ES|QL queries.</returns>
	public static IEsqlQueryable<T> From<T>() where T : class
		=> InMemoryClient.Query<T>();

	/// <summary>
	/// Creates a queryable for ES|QL string generation with explicit index pattern.
	/// </summary>
	/// <typeparam name="T">The document type.</typeparam>
	/// <param name="indexPattern">The Elasticsearch index pattern (e.g., "logs-*").</param>
	/// <returns>A queryable that can be used to build ES|QL queries.</returns>
	public static IEsqlQueryable<T> From<T>(string indexPattern) where T : class
		=> InMemoryClient.Query<T>(indexPattern);
}
