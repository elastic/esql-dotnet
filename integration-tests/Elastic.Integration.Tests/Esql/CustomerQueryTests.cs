// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Integration.Tests;

namespace Elastic.Integration.Tests.Esql;

/// <summary>ES|QL query tests for Customers comparing against LINQ to Objects.</summary>
public class CustomerQueryTests : IntegrationTestBase
{
	[Test]
	public async Task Customers_CountMatches()
	{
		var esqlCount = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.CountAsync();

		var linqCount = TestData.Customers.Count;

		esqlCount.Should().Be(linqCount);
	}

	[Test]
	public async Task Customers_FilterByTier_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.Tier == CustomerTier.Gold)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Customers
			.Where(c => c.Tier == CustomerTier.Gold)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Customers_FilterByVerified_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.IsVerified)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Customers
			.Where(c => c.IsVerified)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Customers_FilterBySubscribed_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.IsSubscribedToNewsletter)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Customers
			.Where(c => c.IsSubscribedToNewsletter)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Customers_FilterByVerifiedAndSubscribed_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.IsVerified && c.IsSubscribedToNewsletter)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Customers
			.Where(c => c.IsVerified && c.IsSubscribedToNewsletter)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Customers_FilterByPlatinumOrDiamond_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.Tier == CustomerTier.Platinum || c.Tier == CustomerTier.Diamond)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Customers
			.Where(c => c.Tier == CustomerTier.Platinum || c.Tier == CustomerTier.Diamond)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Customers_OrderByCreatedAt_Top10()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.OrderByDescending(c => c.CreatedAt)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Customers
			.OrderByDescending(c => c.CreatedAt)
			.Take(10)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Customers_SelectSpecificFields()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Take(5)
			.Select(c => new { c.Id, c.Email, c.Tier })
			.AsEsql()
			.ToListAsync();

		esqlResults.Should().NotBeEmpty();
		esqlResults.Should().HaveCountLessThanOrEqualTo(5);

		// Verify projected fields are populated
		foreach (var result in esqlResults)
		{
			result.Id.Should().NotBeNullOrEmpty();
			result.Email.Should().NotBeNullOrEmpty();
		}
	}

	[Test]
	public async Task Customers_AnyPlatinum_ReturnsExpected()
	{
		var esqlAny = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.Tier == CustomerTier.Platinum)
			.AsEsql()
			.AnyAsync();

		var linqAny = TestData.Customers.Any(c => c.Tier == CustomerTier.Platinum);

		esqlAny.Should().Be(linqAny);
	}

	[Test]
	public async Task Customers_FirstVerified_ReturnsCustomer()
	{
		var esqlFirst = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.IsVerified)
			.AsEsql()
			.FirstAsync();

		esqlFirst.Should().NotBeNull();
		esqlFirst.IsVerified.Should().BeTrue();
	}

	[Test]
	public async Task Customers_TierDistribution_CountsMatch()
	{
		// Count each tier separately and verify totals
		var goldCount = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.Tier == CustomerTier.Gold)
			.AsEsql()
			.CountAsync();

		var silverCount = await Fixture.EsqlClient
			.Query<Customer>("customers*")
			.Where(c => c.Tier == CustomerTier.Silver)
			.AsEsql()
			.CountAsync();

		var linqGoldCount = TestData.Customers.Count(c => c.Tier == CustomerTier.Gold);
		var linqSilverCount = TestData.Customers.Count(c => c.Tier == CustomerTier.Silver);

		goldCount.Should().Be(linqGoldCount);
		silverCount.Should().Be(linqSilverCount);
	}
}
