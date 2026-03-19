// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Esql.Execution;
using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class EdgeCaseTests : IntegrationTestBase
{
	[Test]
	public async Task EmptyResultSet_ReturnsEmptyList()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price < 0)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().BeEmpty();
	}

	[Test]
	public async Task EmptyResultSet_CountIsZero()
	{
		var count = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price < 0)
			.AsEsqlQueryable()
			.CountAsync();

		count.Should().Be(0);
	}

	[Test]
	public async Task SingleRow_Take1_ReturnsExactlyOne()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(1)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
	}

	[Test]
	public async Task NonExistentIndex_ThrowsEsqlExecutionException()
	{
		var act = async () => await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From("non-existent-index-xyz")
			.AsEsqlQueryable()
			.ToListAsync();

		await act.Should().ThrowAsync<EsqlExecutionException>();
	}

	[Test]
	public async Task VeryLargeLimit_ReturnsAllAvailable()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(100000)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(100);
	}

	[Test]
	public async Task WhereMatchingZeroRows_ReturnsEmptyList()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Brand == "NonExistentBrand12345")
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().BeEmpty();
	}

	[Test]
	public async Task NullFieldValues_DeserializedAsNull()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.OrderBy(o => o.Id)
			.AsEsqlQueryable()
			.ToListAsync();

		var expected = TestDataSeeder.Orders.OrderBy(o => o.Id).ToList();

		var foundNull = false;
		for (var i = 0; i < results.Count; i++)
		{
			if (expected[i].Notes is null)
			{
				results[i].Notes.Should().BeNull();
				foundNull = true;
			}
		}

		foundNull.Should().BeTrue("at least one order should have null Notes");
	}

	[Test]
	public async Task NullableIntField_DeserializedAsNull()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestEvent>()
			.From(TestDataSeeder.EventIndex)
			.OrderBy(e => e.Timestamp)
			.AsEsqlQueryable()
			.ToListAsync();

		var expected = TestDataSeeder.Events.OrderBy(e => e.Timestamp).ToList();

		var foundNull = false;
		var foundValue = false;
		for (var i = 0; i < results.Count; i++)
		{
			if (expected[i].DurationNanos is null)
			{
				results[i].DurationNanos.Should().BeNull();
				foundNull = true;
			}
			else
			{
				results[i].DurationNanos.Should().Be(expected[i].DurationNanos);
				foundValue = true;
			}
		}

		foundNull.Should().BeTrue("some events should have null DurationNanos");
		foundValue.Should().BeTrue("some events should have DurationNanos values");
	}

	[Test]
	public async Task FirstAsync_OnEmptySet_Throws()
	{
		var act = async () => await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price < 0)
			.AsEsqlQueryable()
			.FirstAsync();

		await act.Should().ThrowAsync<InvalidOperationException>();
	}

	[Test]
	public async Task SingleAsync_OnMultipleRows_Throws()
	{
		var act = async () => await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(2)
			.AsEsqlQueryable()
			.SingleAsync();

		await act.Should().ThrowAsync<InvalidOperationException>();
	}

	[Test]
	public async Task SingleOrDefaultAsync_OnEmptySet_ReturnsNull()
	{
		var result = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Price < 0)
			.AsEsqlQueryable()
			.SingleOrDefaultAsync();

		result.Should().BeNull();
	}
}
