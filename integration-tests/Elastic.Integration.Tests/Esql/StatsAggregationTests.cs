// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class StatsAggregationTests : IntegrationTestBase
{
	[Test]
	public async Task GroupBy_Count_ByBrand()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.GroupBy(p => p.Brand)
			.Select(g => new { Brand = g.Key, Count = g.Count() })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		var totalCount = results.Sum(r => r.Count);
		totalCount.Should().Be(100);
	}

	[Test]
	public async Task GroupBy_Sum_TotalAmountByStatus()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.GroupBy(o => o.Status)
			.Select(g => new { Status = g.Key, Total = g.Sum(o => o.TotalAmount) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.Total.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task GroupBy_Average_PriceByBrand()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.GroupBy(p => p.Brand)
			.Select(g => new { Brand = g.Key, AvgPrice = g.Average(p => p.Price) })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
			r.AvgPrice.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task GroupBy_Min_MinPriceByBrand()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.GroupBy(p => p.Brand)
			.Select(g => new { Brand = g.Key, MinPrice = g.Min(p => p.Price) })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
			r.MinPrice.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task GroupBy_Max_MaxPriceByBrand()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.GroupBy(p => p.Brand)
			.Select(g => new { Brand = g.Key, MaxPrice = g.Max(p => p.Price) })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
			r.MaxPrice.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task GroupBy_MultipleAggregations()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.GroupBy(p => p.Brand)
			.Select(g => new
			{
				Brand = g.Key,
				Count = g.Count(),
				AvgPrice = g.Average(p => p.Price),
				MinPrice = g.Min(p => p.Price),
				MaxPrice = g.Max(p => p.Price)
			})
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
		{
			r.Count.Should().Be(20);
			r.MinPrice.Should().BeLessThanOrEqualTo(r.AvgPrice);
			r.AvgPrice.Should().BeLessThanOrEqualTo(r.MaxPrice);
		}
	}

	[Test]
	public async Task GroupBy_Enum_GroupsByCategory()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.GroupBy(p => p.Category)
			.Select(g => new { Category = g.Key, Count = g.Count() })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		var totalCount = results.Sum(r => r.Count);
		totalCount.Should().Be(100);
	}

	[Test]
	public async Task GroupBy_String_GroupsByLevel()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestEvent>()
			.From(TestDataSeeder.EventIndex)
			.GroupBy(e => e.Level)
			.Select(g => new { Level = g.Key, Count = g.Count() })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(4);

		var totalCount = results.Sum(r => r.Count);
		totalCount.Should().Be(100);
	}

	[Test]
	public async Task GroupBy_WithWhere_FilteredAggregation()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.InStock)
			.GroupBy(p => p.Brand)
			.Select(g => new { Brand = g.Key, Count = g.Count() })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		var expected = TestDataSeeder.Products
			.Where(p => p.InStock)
			.GroupBy(p => p.Brand)
			.Select(g => new { Brand = g.Key, Count = g.Count() })
			.ToList();

		results.Should().HaveCount(expected.Count);
	}
}
