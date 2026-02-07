// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Integration.Tests.Infrastructure;

/// <summary>
/// Static reference data generated with seed 12345 for LINQ comparison.
/// This data matches what is ingested to Elasticsearch.
/// </summary>
public static class TestData
{
	private const int ProductCount = 1000;
	private const int CustomerCount = 500;
	private const int OrderCount = 5000;
	private const int LogCount = 10000;
	private const int MetricCount = 5000;

	/// <summary>Products generated with seed 12345.</summary>
	public static IReadOnlyList<Product> Products =>
		field ??= ProductGenerator.Generate(ProductCount);

	/// <summary>Customers generated with seed 12345.</summary>
	public static IReadOnlyList<Customer> Customers =>
		field ??= CustomerGenerator.Generate(CustomerCount);

	/// <summary>Orders generated with seed 12345 using product and customer IDs.</summary>
	public static IReadOnlyList<Order> Orders
	{
		get
		{
			if (field != null)
				return field;

			var productIds = Products.Select(p => p.Id).ToList();
			var customerIds = Customers.Select(c => c.Id).ToList();
			field = OrderGenerator.Generate(productIds, customerIds, OrderCount);
			return field;
		}
	}

	/// <summary>Logs generated with seed 12345 with context from other entities.</summary>
	public static IReadOnlyList<ApplicationLog> Logs
	{
		get
		{
			if (field != null)
				return field;

			var productIds = Products.Select(p => p.Id).ToList();
			var customerIds = Customers.Select(c => c.Id).ToList();
			var orderIds = Orders.Select(o => o.Id).ToList();
			field = ApplicationLogGenerator.Generate(orderIds, productIds, customerIds, LogCount);
			return field;
		}
	}

	/// <summary>Metrics generated with seed 12345.</summary>
	public static IReadOnlyList<ApplicationMetric> Metrics =>
		field ??= ApplicationMetricGenerator.Generate(MetricCount);
}
