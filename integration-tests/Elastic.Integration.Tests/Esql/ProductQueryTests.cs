// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Integration.Tests;

namespace Elastic.Integration.Tests.Esql;

/// <summary>ES|QL query tests for Products comparing against LINQ to Objects.</summary>
public class ProductQueryTests : IntegrationTestBase
{
	[Test]
	public async Task Products_CountMatches()
	{
		var esqlCount = await Fixture.EsqlClient
			.Query<Product>("products*")
			.CountAsync();

		var linqCount = TestData.Products.Count;

		esqlCount.Should().Be(linqCount);
	}

	[Test]
	public async Task Products_FilterByInStock_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Product>("products*")
			.Where(p => p.InStock)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Products
			.Where(p => p.InStock)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Products_FilterByBrand_CountMatches()
	{
		const string brand = "TechCorp";

		var esqlResults = await Fixture.EsqlClient
			.Query<Product>("products*")
			.Where(p => p.Brand == brand)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Products
			.Where(p => p.Brand == brand)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Products_FilterByPriceRange_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Product>("products*")
			.Where(p => p.Price >= 100 && p.Price <= 500)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Products
			.Where(p => p.Price is >= 100 and <= 500)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Products_OrderByPrice_Top10Match()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Product>("products*")
			.OrderByDescending(p => p.Price)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Products
			.OrderByDescending(p => p.Price)
			.Take(10)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);

		// Verify ordering - top priced products should match
		for (var i = 0; i < Math.Min(esqlResults.Count, linqResults.Count); i++)
		{
			esqlResults[i].Price.Should().BeApproximately(linqResults[i].Price, 0.01);
		}
	}

	[Test]
	public async Task Products_FilterInStock_OrderByPrice_Top10()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Product>("products*")
			.Where(p => p.InStock)
			.OrderByDescending(p => p.Price)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Products
			.Where(p => p.InStock)
			.OrderByDescending(p => p.Price)
			.Take(10)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);

		// Verify the most expensive in-stock products match
		for (var i = 0; i < Math.Min(esqlResults.Count, linqResults.Count); i++)
		{
			esqlResults[i].Price.Should().BeApproximately(linqResults[i].Price, 0.01);
		}
	}

	[Test]
	public async Task Products_SelectSpecificFields()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Product>("products*")
			.Where(p => p.InStock)
			.Take(5)
			.Select(p => new { p.Id, p.Name, p.Price })
			.AsEsql()
			.ToListAsync();

		esqlResults.Should().NotBeEmpty();
		esqlResults.Should().HaveCountLessThanOrEqualTo(5);

		// Verify projected fields are populated
		foreach (var result in esqlResults)
		{
			result.Id.Should().NotBeNullOrEmpty();
			result.Name.Should().NotBeNullOrEmpty();
			result.Price.Should().BeGreaterThan(0);
		}
	}

	[Test]
	public async Task Products_FilterByMultipleConditions_CountMatches()
	{
		const string brand = "StyleMax";

		var esqlResults = await Fixture.EsqlClient
			.Query<Product>("products*")
			.Where(p => p.Brand == brand && p.InStock && p.Price < 500)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Products
			.Where(p => p.Brand == brand && p.InStock && p.Price < 500)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Products_OrderByBrandThenPrice()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Product>("products*")
			.OrderBy(p => p.Brand)
			.ThenByDescending(p => p.Price)
			.Take(20)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Products
			.OrderBy(p => p.Brand)
			.ThenByDescending(p => p.Price)
			.Take(20)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Products_AnyInStock_ReturnsTrue()
	{
		var esqlAny = await Fixture.EsqlClient
			.Query<Product>("products*")
			.Where(p => p.InStock)
			.AsEsql()
			.AnyAsync();

		var linqAny = TestData.Products.Any(p => p.InStock);

		esqlAny.Should().Be(linqAny);
	}

	[Test]
	public async Task Products_FirstInStock_ReturnsProduct()
	{
		var esqlFirst = await Fixture.EsqlClient
			.Query<Product>("products*")
			.Where(p => p.InStock)
			.AsEsql()
			.FirstAsync();

		esqlFirst.Should().NotBeNull();
		esqlFirst.InStock.Should().BeTrue();
	}
}
