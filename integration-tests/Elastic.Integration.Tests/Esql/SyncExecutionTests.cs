// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class SyncExecutionTests : IntegrationTestBase
{
	[Test]
	public void SyncQuery_ToList_ReturnsAllProducts()
	{
		var results = Fixture.EsqlClient
			.Query<TestProduct>(q => q
				.From(TestDataSeeder.ProductIndex));

		results.Should().HaveCount(100);
	}

	[Test]
	public void SyncQuery_WithWhere_FiltersCorrectly()
	{
		var expected = TestDataSeeder.Products.Count(p => p.InStock);

		var results = Fixture.EsqlClient
			.Query<TestProduct>(q => q
				.From(TestDataSeeder.ProductIndex)
				.Where(p => p.InStock));

		results.Should().HaveCount(expected);
	}

	[Test]
	public void SyncQuery_WithProjection_ReturnsAnonymousType()
	{
		var results = Fixture.EsqlClient
			.Query<TestProduct, object>(q => q
				.From(TestDataSeeder.ProductIndex)
				.Take(5)
				.Select(p => new { p.Id, p.Name }));

		results.Should().HaveCount(5);
	}

	[Test]
	public void SyncQuery_OrderByAndTake_ReturnsOrdered()
	{
		var results = Fixture.EsqlClient
			.Query<TestProduct>(q => q
				.From(TestDataSeeder.ProductIndex)
				.OrderByDescending(p => p.Price)
				.Take(5));

		results.Should().HaveCount(5);
		results.Should().BeInDescendingOrder(p => p.Price);
	}

	[Test]
	public void SyncQuery_Orders_ReturnsAll()
	{
		var results = Fixture.EsqlClient
			.Query<TestOrder>(q => q
				.From(TestDataSeeder.OrderIndex));

		results.Should().HaveCount(100);
	}
}
