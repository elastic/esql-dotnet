// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class WhereFilterTests : IntegrationTestBase
{
	[Test]
	public async Task Where_EqualityString_FiltersByBrand()
	{
		var expected = TestDataSeeder.Products.Count(p => p.Brand == "TechCorp");

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Brand == "TechCorp")
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
		results.Should().AllSatisfy(p => p.Brand.Should().Be("TechCorp"));
	}

	[Test]
	public async Task Where_GreaterThan_FiltersByPrice()
	{
		var expected = TestDataSeeder.Products.Count(p => p.Price > 500);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price > 500)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
		results.Should().AllSatisfy(p => p.Price.Should().BeGreaterThan(500));
	}

	[Test]
	public async Task Where_LessThanOrEqual_FiltersByPrice()
	{
		var expected = TestDataSeeder.Products.Count(p => p.Price <= 100);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price <= 100)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
	}

	[Test]
	public async Task Where_BooleanTrue_FiltersInStock()
	{
		var expected = TestDataSeeder.Products.Count(p => p.InStock);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.InStock)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
		results.Should().AllSatisfy(p => p.InStock.Should().BeTrue());
	}

	[Test]
	public async Task Where_BooleanFalse_FiltersOutOfStock()
	{
		var expected = TestDataSeeder.Products.Count(p => !p.InStock);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => !p.InStock)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
		results.Should().AllSatisfy(p => p.InStock.Should().BeFalse());
	}

	[Test]
	public async Task Where_AndCombination_FiltersMultipleConditions()
	{
		var expected = TestDataSeeder.Products
			.Count(p => p.InStock && p.Price > 200);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.InStock && p.Price > 200)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
	}

	[Test]
	public async Task Where_OrCombination_FiltersEitherCondition()
	{
		var expected = TestDataSeeder.Products
			.Count(p => p.Brand is "TechCorp" or "StyleMax");

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Brand == "TechCorp" || p.Brand == "StyleMax")
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
	}

	[Test]
	public async Task Where_EnumEquality_FiltersByStatus()
	{
		var expected = TestDataSeeder.Orders
			.Count(o => o.Status == OrderStatus.Delivered);

		var results = await Fixture.EsqlClient
			.Query<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.Where(o => o.Status == OrderStatus.Delivered)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
		results.Should().AllSatisfy(o => o.Status.Should().Be(OrderStatus.Delivered));
	}

	[Test]
	public async Task Where_EnumEquality_FiltersByCategory()
	{
		var expected = TestDataSeeder.Products
			.Count(p => p.Category == ProductCategory.Electronics);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Category == ProductCategory.Electronics)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
		results.Should().AllSatisfy(p => p.Category.Should().Be(ProductCategory.Electronics));
	}

	[Test]
	public async Task Where_IntegerComparison_FiltersByStockQuantity()
	{
		var expected = TestDataSeeder.Products
			.Count(p => p.StockQuantity >= 100);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.StockQuantity >= 100)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
	}

	[Test]
	public async Task Where_RangeFilter_PriceBetween()
	{
		var expected = TestDataSeeder.Products
			.Count(p => p.Price is >= 100 and <= 300);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price >= 100 && p.Price <= 300)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
	}

	[Test]
	public async Task Where_StringEquality_FiltersByLevel()
	{
		var expected = TestDataSeeder.Events
			.Count(e => e.Level == "Error");

		var results = await Fixture.EsqlClient
			.Query<TestEvent>()
			.From(TestDataSeeder.EventIndex)
			.Where(e => e.Level == "Error")
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
		results.Should().AllSatisfy(e => e.Level.Should().Be("Error"));
	}

	[Test]
	public async Task Where_DecimalComparison_FiltersByTotalAmount()
	{
		var expected = TestDataSeeder.Orders
			.Count(o => o.TotalAmount > 500);

		var results = await Fixture.EsqlClient
			.Query<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.Where(o => o.TotalAmount > 500)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
	}

	[Test]
	public async Task Where_MultipleEnumValues_OrFilter()
	{
		var expected = TestDataSeeder.Orders
			.Count(o => o.Status is OrderStatus.Pending or OrderStatus.Cancelled);

		var results = await Fixture.EsqlClient
			.Query<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Cancelled)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
	}

	[Test]
	public async Task Where_ComplexAndOr_MultipleConditions()
	{
		var expected = TestDataSeeder.Products
			.Count(p => (p.Brand == "TechCorp" || p.Brand == "SportPro") && p.InStock);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => (p.Brand == "TechCorp" || p.Brand == "SportPro") && p.InStock)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected);
	}
}
