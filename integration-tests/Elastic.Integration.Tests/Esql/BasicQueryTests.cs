// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class BasicQueryTests : IntegrationTestBase
{
	[Test]
	public async Task ToListAsync_ReturnsAllProducts()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(100);
	}

	[Test]
	public async Task ToArrayAsync_ReturnsAllProducts()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.AsEsql()
			.ToArrayAsync();

		results.Should().HaveCount(100);
	}

	[Test]
	public async Task CountAsync_ReturnsCorrectCount()
	{
		var count = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.AsEsql()
			.CountAsync();

		count.Should().Be(100);
	}

	[Test]
	public async Task AnyAsync_ReturnsTrueWhenDataExists()
	{
		var any = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.AsEsql()
			.AnyAsync();

		any.Should().BeTrue();
	}

	[Test]
	public async Task AnyAsync_ReturnsFalseForImpossibleFilter()
	{
		var any = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price < 0)
			.AsEsql()
			.AnyAsync();

		any.Should().BeFalse();
	}

	[Test]
	public async Task FirstAsync_ReturnsFirstProduct()
	{
		var first = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.AsEsql()
			.FirstAsync();

		first.Should().NotBeNull();
		first.Id.Should().NotBeNullOrEmpty();
	}

	[Test]
	public async Task FirstOrDefaultAsync_ReturnsNullForNoMatches()
	{
		var result = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price < 0)
			.AsEsql()
			.FirstOrDefaultAsync();

		result.Should().BeNull();
	}

	[Test]
	public async Task SingleAsync_WithTake1_ReturnsSingleProduct()
	{
		var result = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(1)
			.AsEsql()
			.SingleAsync();

		result.Should().NotBeNull();
		result.Id.Should().NotBeNullOrEmpty();
	}

	[Test]
	public async Task CountAsync_Orders_ReturnsCorrectCount()
	{
		var count = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.AsEsql()
			.CountAsync();

		count.Should().Be(100);
	}

	[Test]
	public async Task CountAsync_Events_ReturnsCorrectCount()
	{
		var count = await Fixture.EsqlClient
			.CreateQuery<TestEvent>()
			.From(TestDataSeeder.EventIndex)
			.AsEsql()
			.CountAsync();

		count.Should().Be(100);
	}

	[Test]
	public async Task AsAsyncEnumerable_StreamsResults()
	{
		var count = 0;
		await foreach (var product in Fixture.EsqlClient
						   .CreateQuery<TestProduct>()
						   .From(TestDataSeeder.ProductIndex)
						   .AsEsql()
						   .AsAsyncEnumerable())
		{
			product.Should().NotBeNull();
			count++;
		}

		count.Should().Be(100);
	}
}
