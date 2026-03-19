// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using static Elastic.Esql.Functions.EsqlFunctions;

namespace Elastic.Esql.Integration.Tests.Esql;

public class MatchQueryTests : IntegrationTestBase
{
	[Test]
	public async Task Match_ProductName_ReturnsMatchingProducts()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => Match(p.Name, "Product 1"))
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results.Should().AllSatisfy(p => p.Name.Should().Contain("Product"));
	}

	[Test]
	public async Task Match_EventMessage_ReturnsMatchingEvents()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestEvent>()
			.From(TestDataSeeder.EventIndex)
			.Where(e => Match(e.Message, "operation completed"))
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results.Should().AllSatisfy(e => e.Message.Should().Contain("operation completed"));
	}

	[Test]
	public async Task Match_NoResults_ReturnsEmpty()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => Match(p.Name, "xyznonexistent99999"))
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().BeEmpty();
	}

	[Test]
	public async Task Match_CombinedWithWhere_ReturnsFilteredResults()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => Match(p.Name, "Product"))
			.Where(p => p.InStock)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results.Should().AllSatisfy(p =>
		{
			p.InStock.Should().BeTrue();
			p.Name.Should().Contain("Product");
		});
	}
}
