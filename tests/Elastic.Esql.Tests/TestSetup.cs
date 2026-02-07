// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests;

/// <summary>
/// Base class for ES|QL tests providing an in-memory client for string generation.
/// </summary>
public abstract class EsqlTestBase
{
	protected static readonly EsqlClient Client = new(
		EsqlClientSettings.InMemory(EsqlTestMappingContext.Instance)
	);

	static EsqlTestBase() => Esql.Configure(EsqlTestMappingContext.Instance);
}
