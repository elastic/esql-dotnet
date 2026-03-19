// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Integration.Tests.Esql;

public class QueryOptionsTests : IntegrationTestBase
{
	[Test]
	public async Task WithOptions_AllowPartialResults_ReturnsResults()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.WithOptions(new EsqlQueryOptions { AllowPartialResults = true })
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(5);
	}

	[Test]
	public async Task WithOptions_AllowPartialResults_AsyncQuery_ReturnsResults()
	{
		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.WithOptions(new EsqlQueryOptions { AllowPartialResults = true })
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync();

		var results = asyncQuery.AsEnumerable().ToList();

		results.Should().HaveCount(5);
	}
}
