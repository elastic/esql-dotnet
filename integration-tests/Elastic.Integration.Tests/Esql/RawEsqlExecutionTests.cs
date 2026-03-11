// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class RawEsqlExecutionTests : IntegrationTestBase
{
	[Test]
	public void RawEsql_Sync_ToList_ReturnsLimitedRows()
	{
		var results = Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.RawEsql("LIMIT 5")
			.AsEsql()
			.ToList();

		results.Should().HaveCount(5);
	}

	[Test]
	public async Task RawEsql_Async_AsAsyncEnumerable_StreamsRows()
	{
		var results = new List<TestProduct>();

		await foreach (var product in Fixture.EsqlClient
						   .CreateQuery<TestProduct>()
						   .From(TestDataSeeder.ProductIndex)
						   .RawEsql(
							   """
                               | WHERE in_stock == true
                               | LIMIT 7
                               """
						   )
						   .AsEsql()
						   .AsAsyncEnumerable())
		{
			results.Add(product);
		}

		results.Should().HaveCount(7);
		results.Should().AllSatisfy(p => p.InStock.Should().BeTrue());
	}

	[Test]
	public async Task RawEsql_AsyncQuery_ToAsyncQueryAsync_ReturnsRows()
	{
		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.RawEsql("LIMIT 4")
			.AsEsql()
			.ToAsyncQueryAsync();

		var results = await asyncQuery.ToListAsync();

		results.Should().HaveCount(4);
	}

	[Test]
	public void RawEsql_AsyncQuery_ToAsyncQuery_ReturnsRows()
	{
		using var asyncQuery = Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.RawEsql("LIMIT 3")
			.AsEsql()
			.ToAsyncQuery();

		var results = asyncQuery.ToList();

		results.Should().HaveCount(3);
	}

	[Test]
	public async Task RawEsql_TypeShift_MaterializesTargetType()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.RawEsql<TestProduct, RawProductSummary>(
				"""
                | KEEP product_id, name
                | LIMIT 6
                """
			)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(6);
		results.Should().AllSatisfy(p =>
		{
			p.Id.Should().NotBeNullOrEmpty();
			p.Name.Should().NotBeNullOrEmpty();
		});
	}
}
