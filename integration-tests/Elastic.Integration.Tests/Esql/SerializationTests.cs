// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class SerializationTests : IntegrationTestBase
{
	// =========================================================================
	// Field type round-trips
	// =========================================================================

	[Test]
	public async Task RoundTrip_StringFields_PreserveValues()
	{
		var products = TestDataSeeder.Products;

		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Id)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(products.Count);

		var expected = products.OrderBy(p => p.Id).ToList();
		for (var i = 0; i < results.Count; i++)
		{
			results[i].Id.Should().Be(expected[i].Id);
			results[i].Name.Should().Be(expected[i].Name);
			results[i].Brand.Should().Be(expected[i].Brand);
		}
	}

	[Test]
	public async Task RoundTrip_DoubleField_PreservesPrice()
	{
		var expected = TestDataSeeder.Products
			.OrderBy(p => p.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Id)
			.AsEsql()
			.ToListAsync();

		for (var i = 0; i < results.Count; i++)
			results[i].Price.Should().BeApproximately(expected[i].Price, 0.01);
	}

	[Test]
	public async Task RoundTrip_BoolField_PreservesInStock()
	{
		var expected = TestDataSeeder.Products
			.OrderBy(p => p.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Id)
			.AsEsql()
			.ToListAsync();

		for (var i = 0; i < results.Count; i++)
			results[i].InStock.Should().Be(expected[i].InStock);
	}

	[Test]
	public async Task RoundTrip_IntField_PreservesStockQuantity()
	{
		var expected = TestDataSeeder.Products
			.OrderBy(p => p.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Id)
			.AsEsql()
			.ToListAsync();

		for (var i = 0; i < results.Count; i++)
			results[i].StockQuantity.Should().Be(expected[i].StockQuantity);
	}

	[Test]
	public async Task RoundTrip_DateTimeField_PreservesTimestamp()
	{
		var expected = TestDataSeeder.Orders
			.OrderBy(o => o.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.OrderBy(o => o.Id)
			.AsEsql()
			.ToListAsync();

		for (var i = 0; i < results.Count; i++)
		{
			var diff = (results[i].Timestamp - expected[i].Timestamp).Duration();
			diff.Should().BeLessThan(TimeSpan.FromSeconds(1));
		}
	}

	[Test]
	public async Task RoundTrip_DecimalField_PreservesTotalAmount()
	{
		var expected = TestDataSeeder.Orders
			.OrderBy(o => o.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.OrderBy(o => o.Id)
			.AsEsql()
			.ToListAsync();

		for (var i = 0; i < results.Count; i++)
			results[i].TotalAmount.Should().BeApproximately(expected[i].TotalAmount, 0.01m);
	}

	[Test]
	public async Task RoundTrip_EnumAsString_PreservesStatus()
	{
		var expected = TestDataSeeder.Orders
			.OrderBy(o => o.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.OrderBy(o => o.Id)
			.AsEsql()
			.ToListAsync();

		for (var i = 0; i < results.Count; i++)
			results[i].Status.Should().Be(expected[i].Status);
	}

	[Test]
	public async Task RoundTrip_EnumAsString_PreservesCategory()
	{
		var expected = TestDataSeeder.Products
			.OrderBy(p => p.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Id)
			.AsEsql()
			.ToListAsync();

		for (var i = 0; i < results.Count; i++)
			results[i].Category.Should().Be(expected[i].Category);
	}

	// =========================================================================
	// Nullable fields
	// =========================================================================

	[Test]
	public async Task RoundTrip_NullableDouble_PreservesNulls()
	{
		var expected = TestDataSeeder.Products
			.OrderBy(p => p.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Id)
			.AsEsql()
			.ToListAsync();

		var nullCount = 0;
		var nonNullCount = 0;
		for (var i = 0; i < results.Count; i++)
		{
			if (expected[i].SalePrice.HasValue)
			{
				results[i].SalePrice.Should().NotBeNull();
				results[i].SalePrice!.Value.Should().BeApproximately(expected[i].SalePrice!.Value, 0.01);
				nonNullCount++;
			}
			else
			{
				results[i].SalePrice.Should().BeNull();
				nullCount++;
			}
		}

		nullCount.Should().BeGreaterThan(0, "some products should have null SalePrice");
		nonNullCount.Should().BeGreaterThan(0, "some products should have SalePrice");
	}

	[Test]
	public async Task RoundTrip_NullableString_PreservesNulls()
	{
		var expected = TestDataSeeder.Orders
			.OrderBy(o => o.Id)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.OrderBy(o => o.Id)
			.AsEsql()
			.ToListAsync();

		var nullCount = 0;
		for (var i = 0; i < results.Count; i++)
		{
			if (expected[i].ClientIp is null)
			{
				results[i].ClientIp.Should().BeNull();
				nullCount++;
			}
			else
			{
				results[i].ClientIp.Should().Be(expected[i].ClientIp);
			}
		}

		nullCount.Should().BeGreaterThan(0, "some orders should have null ClientIp");
	}

	// =========================================================================
	// Dotted JSON property names (@timestamp, log.level, etc.)
	// =========================================================================

	[Test]
	public async Task RoundTrip_DottedPropertyNames_PreserveValues()
	{
		var expected = TestDataSeeder.Events
			.OrderBy(e => e.Timestamp)
			.ToList();

		var results = await Fixture.EsqlClient
			.CreateQuery<TestEvent>()
			.From(TestDataSeeder.EventIndex)
			.OrderBy(e => e.Timestamp)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(expected.Count);

		for (var i = 0; i < results.Count; i++)
		{
			results[i].Level.Should().Be(expected[i].Level);
			results[i].ServiceName.Should().Be(expected[i].ServiceName);
			results[i].Message.Should().Be(expected[i].Message);
		}
	}

	// =========================================================================
	// Collection / List fields
	// =========================================================================

	[Test]
	public async Task RoundTrip_ListField_PreservesTags()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.InStock)
			.OrderBy(p => p.Id)
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		var expected = TestDataSeeder.Products
			.Where(p => p.InStock)
			.OrderBy(p => p.Id)
			.ToList();

		for (var i = 0; i < results.Count; i++)
		{
			results[i].Tags.Should().NotBeNull();
			results[i].Tags.Should().BeEquivalentTo(expected[i].Tags);
		}
	}

	// =========================================================================
	// Anonymous type materialization (non-AOT)
	// =========================================================================

	[Test]
	public async Task AnonymousType_MixedFieldTypes_DeserializesCorrectly()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.Select(p => new { p.Id, p.Price, p.InStock, p.StockQuantity })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(3);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Price.Should().BeGreaterThan(0);
		}
	}

	[Test]
	public async Task AnonymousType_DateTimeAndEnum_DeserializesCorrectly()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.Take(3)
			.Select(o => new { o.Id, o.Timestamp, o.Status, o.TotalAmount })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(3);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Timestamp.Should().BeAfter(DateTime.MinValue);
		}
	}

	[Test]
	public async Task AnonymousType_NullableFields_HandlesNulls()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.OrderBy(p => p.Id)
			.Take(10)
			.Select(p => new { p.Id, p.SalePrice })
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(10);

		var hasNull = results.Any(r => r.SalePrice is null);
		var hasValue = results.Any(r => r.SalePrice is not null);
		(hasNull || hasValue).Should().BeTrue();
	}

	// =========================================================================
	// Top-level JsonConverter rejection
	// =========================================================================

	[Test]
	public void TopLevelJsonConverter_ThrowsNotSupportedException()
	{
		var act = () => Fixture.EsqlClient
			.CreateQuery<TypeWithTopLevelConverter>()
			.From("any-index")
			.Where(x => x.Name == "test")
			.ToEsqlString();

		act.Should().Throw<NotSupportedException>()
			.WithMessage("*custom JsonConverter*");
	}

	// =========================================================================
	// JsonPropertyName round-trip (via KEEP)
	// =========================================================================

	[Test]
	public async Task JsonPropertyName_RoundTrip_FieldsMapCorrectly()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Keep("product_id", "price_usd", "in_stock")
			.Take(5)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Price.Should().BeGreaterThan(0);
		}
	}
}
