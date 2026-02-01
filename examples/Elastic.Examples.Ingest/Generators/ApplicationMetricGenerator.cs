// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Bogus;
using Elastic.Examples.Domain.Models;

namespace Elastic.Examples.Ingest.Generators;

/// <summary>Generates fake ApplicationMetric data for testing and demos.</summary>
public static class ApplicationMetricGenerator
{
	private const int Seed = 12345;

	private static readonly string[] Services = ["api-gateway", "order-service", "payment-service", "inventory-service", "notification-service"];
	private static readonly string[] MetricSets = ["system", "app", "business", "database"];
	private static readonly string[] HostNames = ["web-01", "web-02", "api-01", "api-02", "worker-01"];
	private static readonly string[] Endpoints = ["/api/orders", "/api/products", "/api/customers", "/api/payments", "/api/inventory"];
	private static readonly string[] Methods = ["GET", "POST", "PUT", "DELETE"];
	private static readonly string[] Databases = ["orders_db", "products_db", "customers_db", "analytics_db"];
	private static readonly string[] Regions = ["us-east-1", "us-west-2", "eu-west-1", "ap-southeast-1"];

	public static IReadOnlyList<ApplicationMetric> Generate(int count = 5000)
	{
		Randomizer.Seed = new Random(Seed);

		var labelsFaker = new Faker<MetricLabels>()
			.RuleFor(l => l.Endpoint, f => f.Random.Bool(0.6f) ? f.PickRandom(Endpoints) : null)
			.RuleFor(l => l.Method, (f, l) => l.Endpoint != null ? f.PickRandom(Methods) : null)
			.RuleFor(l => l.Database, f => f.Random.Bool(0.3f) ? f.PickRandom(Databases) : null)
			.RuleFor(l => l.Region, f => f.PickRandom(Regions));

		var metricFaker = new Faker<ApplicationMetric>()
			.RuleFor(m => m.Timestamp, f => f.Date.Recent(7))
			.RuleFor(m => m.MetricSetName, f => f.PickRandom(MetricSets))
			.RuleFor(m => m.ServiceName, f => f.PickRandom(Services))
			.RuleFor(m => m.ServiceVersion, f => $"{f.Random.Int(1, 3)}.{f.Random.Int(0, 12)}.{f.Random.Int(0, 99)}")
			.RuleFor(m => m.HostName, f => f.PickRandom(HostNames))
			.RuleFor(m => m.HostIp, f => f.Internet.IpAddress().ToString())
			// System metrics
			.RuleFor(m => m.CpuPercent, (f, m) => m.MetricSetName == "system" ? f.Random.Double(5, 95) : null)
			.RuleFor(m => m.MemoryPercent, (f, m) => m.MetricSetName == "system" ? f.Random.Double(30, 85) : null)
			.RuleFor(m => m.MemoryUsedBytes, (f, m) => m.MetricSetName == "system" ? f.Random.Long(1_000_000_000, 8_000_000_000) : null)
			.RuleFor(m => m.MemoryTotalBytes, (f, m) => m.MetricSetName == "system" ? 16_000_000_000L : null)
			// App metrics
			.RuleFor(m => m.RequestsTotal, (f, m) => m.MetricSetName == "app" ? f.Random.Long(10_000, 1_000_000) : null)
			.RuleFor(m => m.RequestsPerSecond, (f, m) => m.MetricSetName == "app" ? f.Random.Double(10, 500) : null)
			.RuleFor(m => m.ErrorsTotal, (f, m) => m.MetricSetName == "app" ? f.Random.Long(0, 1000) : null)
			.RuleFor(m => m.LatencyAvgMs, (f, m) => m.MetricSetName == "app" ? f.Random.Double(5, 200) : null)
			.RuleFor(m => m.LatencyP50Ms, (f, m) => m.MetricSetName == "app" ? f.Random.Double(2, 100) : null)
			.RuleFor(m => m.LatencyP95Ms, (f, m) => m.MetricSetName == "app" ? f.Random.Double(50, 500) : null)
			.RuleFor(m => m.LatencyP99Ms, (f, m) => m.MetricSetName == "app" ? f.Random.Double(100, 1000) : null)
			// Business metrics
			.RuleFor(m => m.OrdersCount, (f, m) => m.MetricSetName == "business" ? f.Random.Long(0, 500) : null)
			.RuleFor(m => m.OrdersValue, (f, m) => m.MetricSetName == "business" ? f.Random.Double(0, 50_000) : null)
			.RuleFor(m => m.CartAbandonments, (f, m) => m.MetricSetName == "business" ? f.Random.Long(0, 100) : null)
			.RuleFor(m => m.ActiveUsers, (f, m) => m.MetricSetName == "business" ? f.Random.Long(100, 10_000) : null)
			// Database metrics
			.RuleFor(m => m.DbConnectionsActive, (f, m) => m.MetricSetName == "database" ? f.Random.Int(5, 50) : null)
			.RuleFor(m => m.DbConnectionPoolSize, (f, m) => m.MetricSetName == "database" ? 100 : null)
			.RuleFor(m => m.DbQueryTimeAvgMs, (f, m) => m.MetricSetName == "database" ? f.Random.Double(1, 100) : null)
			// Labels
			.RuleFor(m => m.Labels, f => f.Random.Bool(0.7f) ? labelsFaker.Generate() : null);

		return metricFaker.Generate(count);
	}
}
