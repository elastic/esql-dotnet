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

	private static IReadOnlyList<Product>? _products;
	private static IReadOnlyList<Customer>? _customers;
	private static IReadOnlyList<Order>? _orders;
	private static IReadOnlyList<ApplicationLog>? _logs;
	private static IReadOnlyList<ApplicationMetric>? _metrics;

	/// <summary>Products generated with seed 12345.</summary>
	public static IReadOnlyList<Product> Products =>
		_products ??= ProductGenerator.Generate(ProductCount);

	/// <summary>Customers generated with seed 12345.</summary>
	public static IReadOnlyList<Customer> Customers =>
		_customers ??= CustomerGenerator.Generate(CustomerCount);

	/// <summary>Orders generated with seed 12345 using product and customer IDs.</summary>
	public static IReadOnlyList<Order> Orders
	{
		get
		{
			if (_orders != null)
				return _orders;

			var productIds = Products.Select(p => p.Id).ToList();
			var customerIds = Customers.Select(c => c.Id).ToList();
			_orders = OrderGenerator.Generate(productIds, customerIds, OrderCount);
			return _orders;
		}
	}

	/// <summary>Logs generated with seed 12345 with context from other entities.</summary>
	public static IReadOnlyList<ApplicationLog> Logs
	{
		get
		{
			if (_logs != null)
				return _logs;

			var productIds = Products.Select(p => p.Id).ToList();
			var customerIds = Customers.Select(c => c.Id).ToList();
			var orderIds = Orders.Select(o => o.Id).ToList();
			_logs = ApplicationLogGenerator.Generate(orderIds, productIds, customerIds, LogCount);
			return _logs;
		}
	}

	/// <summary>Metrics generated with seed 12345.</summary>
	public static IReadOnlyList<ApplicationMetric> Metrics =>
		_metrics ??= ApplicationMetricGenerator.Generate(MetricCount);
}
