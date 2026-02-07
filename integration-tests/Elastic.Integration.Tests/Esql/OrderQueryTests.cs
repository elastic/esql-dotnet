// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Integration.Tests;

namespace Elastic.Integration.Tests.Esql;

/// <summary>ES|QL query tests for Orders comparing against LINQ to Objects.</summary>
public class OrderQueryTests : IntegrationTestBase
{
	[Test]
	public async Task Orders_CountMatches()
	{
		var esqlCount = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.CountAsync();

		var linqCount = TestData.Orders.Count;

		esqlCount.Should().Be(linqCount);
	}

	[Test]
	public async Task Orders_FilterByStatus_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Status == OrderStatus.Delivered)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Orders
			.Where(o => o.Status == OrderStatus.Delivered)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Orders_FilterByPending_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Status == OrderStatus.Pending)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Orders
			.Where(o => o.Status == OrderStatus.Pending)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Orders_FilterByCurrency_CountMatches()
	{
		const string currency = "USD";

		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Currency == currency)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Orders
			.Where(o => o.Currency == currency)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Orders_FilterByTotalAmountRange_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.TotalAmount >= 100 && o.TotalAmount <= 500)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Orders
			.Where(o => o.TotalAmount is >= 100 and <= 500)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Orders_OrderByTotalAmount_Top10Match()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.OrderByDescending(o => o.TotalAmount)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Orders
			.OrderByDescending(o => o.TotalAmount)
			.Take(10)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);

		// Verify ordering - top orders by amount should be similar
		for (var i = 0; i < Math.Min(esqlResults.Count, linqResults.Count); i++)
		{
			esqlResults[i].TotalAmount.Should().BeApproximately(linqResults[i].TotalAmount, 0.01m);
		}
	}

	[Test]
	public async Task Orders_FilterByTimestamp_RecentOrders()
	{
		var cutoffDate = DateTime.UtcNow.AddDays(-30);

		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Timestamp >= cutoffDate)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Orders
			.Where(o => o.Timestamp >= cutoffDate)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Orders_FilterDeliveredWithHighValue_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Status == OrderStatus.Delivered && o.TotalAmount > 200)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Orders
			.Where(o => o.Status == OrderStatus.Delivered && o.TotalAmount > 200)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Orders_SelectSpecificFields()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Take(5)
			.Select(o => new { o.Id, o.CustomerId, o.TotalAmount, o.Status })
			.AsEsql()
			.ToListAsync();

		esqlResults.Should().NotBeEmpty();
		esqlResults.Should().HaveCountLessThanOrEqualTo(5);

		// Verify projected fields are populated
		foreach (var result in esqlResults)
		{
			result.Id.Should().NotBeNullOrEmpty();
			result.CustomerId.Should().NotBeNullOrEmpty();
			result.TotalAmount.Should().BeGreaterThan(0);
		}
	}

	[Test]
	public async Task Orders_StatusDistribution_CountsMatch()
	{
		// Count specific statuses and verify
		var deliveredCount = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Status == OrderStatus.Delivered)
			.AsEsql()
			.CountAsync();

		var shippedCount = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Status == OrderStatus.Shipped)
			.AsEsql()
			.CountAsync();

		var cancelledCount = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Status == OrderStatus.Cancelled)
			.AsEsql()
			.CountAsync();

		var linqDeliveredCount = TestData.Orders.Count(o => o.Status == OrderStatus.Delivered);
		var linqShippedCount = TestData.Orders.Count(o => o.Status == OrderStatus.Shipped);
		var linqCancelledCount = TestData.Orders.Count(o => o.Status == OrderStatus.Cancelled);

		deliveredCount.Should().Be(linqDeliveredCount);
		shippedCount.Should().Be(linqShippedCount);
		cancelledCount.Should().Be(linqCancelledCount);
	}

	[Test]
	public async Task Orders_AnyWithPromoCode_ReturnsExpected()
	{
		// Note: Checking if PromoCodes is not empty
		var esqlResults = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.AsEsql()
			.ToListAsync();

		var linqAny = TestData.Orders.Any(o => o.PromoCodes.Count > 0);
		var esqlAny = esqlResults.Any(o => o.PromoCodes.Count > 0);

		esqlAny.Should().Be(linqAny);
	}

	[Test]
	public async Task Orders_FirstDelivered_ReturnsOrder()
	{
		var esqlFirst = await Fixture.EsqlClient
			.Query<Order>("orders-*")
			.Where(o => o.Status == OrderStatus.Delivered)
			.AsEsql()
			.FirstAsync();

		esqlFirst.Should().NotBeNull();
		esqlFirst.Status.Should().Be(OrderStatus.Delivered);
	}
}
