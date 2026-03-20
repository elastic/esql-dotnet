// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class SelectProjectionTests : IntegrationTestBase
{
	[Test]
	public async Task Select_AnonymousType_ProjectsFields()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.InStock)
			.Take(5)
			.Select(p => new { p.Id, p.Name, p.Price })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results.Should().HaveCountLessThanOrEqualTo(5);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Name.Should().NotBeNullOrEmpty();
			r.Price.Should().BeGreaterThan(0);
		}
	}

	[Test]
	public async Task Select_AnonymousType_WithMultipleTypes()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.Select(p => new { p.Name, p.Price, p.InStock, p.StockQuantity, p.Category })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(3);

		foreach (var r in results)
		{
			r.Name.Should().NotBeNullOrEmpty();
			r.Price.Should().BeGreaterThan(0);
		}
	}

	[Test]
	public async Task Select_AnonymousType_OrderFields()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.Take(5)
			.Select(o => new { o.Id, o.CustomerId, o.TotalAmount, o.Status })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.CustomerId.Should().NotBeNullOrEmpty();
			r.TotalAmount.Should().BeGreaterThan(0);
		}
	}

	[Test]
	public async Task Select_AnonymousType_EventDottedFields()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestEvent>()
			.From(TestDataSeeder.EventIndex)
			.Take(5)
			.Select(e => new { e.Timestamp, e.Level, e.ServiceName, e.Message })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
		{
			r.Level.Should().NotBeNullOrEmpty();
			r.ServiceName.Should().NotBeNullOrEmpty();
			r.Message.Should().NotBeNullOrEmpty();
		}
	}

	[Test]
	public async Task Select_FieldSubset_ReducesColumns()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.Select(p => new { p.Id, p.Brand })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(3);

		foreach (var r in results)
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Brand.Should().NotBeNullOrEmpty();
		}
	}
}
