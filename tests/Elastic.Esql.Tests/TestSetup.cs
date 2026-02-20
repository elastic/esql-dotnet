// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.FieldMetadataResolver;

namespace Elastic.Esql.Tests;

/// <summary>
/// Base class for ES|QL tests providing an in-memory client for string generation.
/// </summary>
public abstract class EsqlTestBase
{
	protected static readonly EsqlQueryProvider QueryProvider = new(
		new SystemTextJsonFieldMetadataResolver(
			new JsonSerializerOptions
			{
				TypeInfoResolver = EsqlTestMappingContext.Default,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			}
		)
	);

	protected static EsqlQueryable<T> CreateQuery<T>() => new(QueryProvider);
}
