// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class KeepDropTests : IntegrationTestBase
{
	[Test]
	public async Task Keep_StringFieldNames_KeepsOnlySpecified()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Keep("product_id", "name", "price_usd")
			.Take(5)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Name.Should().NotBeNullOrEmpty();
			r.Price.Should().BeGreaterThan(0);
			// Fields not in KEEP should be default values
			r.Brand.Should().BeNullOrEmpty();
		}
	}

	[Test]
	public async Task Keep_LambdaSelectors_KeepsOnlySpecified()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Keep(p => p.Id, p => p.Name)
			.Take(5)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Name.Should().NotBeNullOrEmpty();
		}
	}

	[Test]
	public async Task Drop_StringFieldNames_RemovesSpecified()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Drop("tags", "sale_price_usd", "created_at")
			.Take(5)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Name.Should().NotBeNullOrEmpty();
			// Dropped fields should not be populated
			r.Tags.Should().BeEmpty();
		}
	}

	[Test]
	public async Task Drop_LambdaSelectors_RemovesSpecified()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Drop(p => p.Tags, p => p.SalePrice)
			.Take(5)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(5);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Name.Should().NotBeNullOrEmpty();
		}
	}
}
