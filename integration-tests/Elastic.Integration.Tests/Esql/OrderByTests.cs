// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class OrderByTests : IntegrationTestBase
{
	[Test]
	public async Task OrderBy_Ascending_SortsByPrice()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Price)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(10);
		results.Should().BeInAscendingOrder(p => p.Price);
	}

	[Test]
	public async Task OrderByDescending_SortsByPriceDesc()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderByDescending(p => p.Price)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(10);
		results.Should().BeInDescendingOrder(p => p.Price);
	}

	[Test]
	public async Task OrderByDescending_Top10Prices_MatchLinq()
	{
		var expected = TestDataSeeder.Products
			.OrderByDescending(p => p.Price)
			.Take(10)
			.Select(p => p.Price)
			.ToList();

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderByDescending(p => p.Price)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		for (var i = 0; i < results.Count; i++)
			results[i].Price.Should().BeApproximately(expected[i], 0.01);
	}

	[Test]
	public async Task ThenBy_MultiColumnSort()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Brand)
			.ThenByDescending(p => p.Price)
			.Take(20)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(20);

		var expected = TestDataSeeder.Products
			.OrderBy(p => p.Brand)
			.ThenByDescending(p => p.Price)
			.Take(20)
			.ToList();

		for (var i = 0; i < results.Count; i++)
		{
			results[i].Brand.Should().Be(expected[i].Brand);
			results[i].Price.Should().BeApproximately(expected[i].Price, 0.01);
		}
	}

	[Test]
	public async Task OrderBy_WithWhere_SortedAndFiltered()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.InStock)
			.OrderByDescending(p => p.Price)
			.Take(5)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCountLessThanOrEqualTo(5);
		results.Should().BeInDescendingOrder(p => p.Price);
		results.Should().AllSatisfy(p => p.InStock.Should().BeTrue());
	}

	[Test]
	public async Task OrderBy_DateTime_SortsByTimestamp()
	{
		var results = await Fixture.EsqlClient
			.Query<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.OrderByDescending(o => o.Timestamp)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(10);
		results.Should().BeInDescendingOrder(o => o.Timestamp);
	}

	[Test]
	public async Task OrderBy_Integer_SortsByStockQuantity()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderByDescending(p => p.StockQuantity)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(10);
		results.Should().BeInDescendingOrder(p => p.StockQuantity);
	}
}
