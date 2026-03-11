// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class LimitTests : IntegrationTestBase
{
	[Test]
	public async Task Take_LimitsResultCount()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(10);
	}

	[Test]
	public async Task Take_WithWhere_LimitsFilteredResults()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.InStock)
			.Take(5)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCountLessThanOrEqualTo(5);
		results.Should().AllSatisfy(p => p.InStock.Should().BeTrue());
	}

	[Test]
	public async Task Take_WithOrderBy_LimitsSortedResults()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderByDescending(p => p.Price)
			.Take(3)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(3);
		results.Should().BeInDescendingOrder(p => p.Price);
	}

	[Test]
	public async Task Take_One_ReturnsSingleRow()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(1)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(1);
	}

	[Test]
	public async Task Take_LargerThanDataset_ReturnsAll()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(10000)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(100);
	}
}
