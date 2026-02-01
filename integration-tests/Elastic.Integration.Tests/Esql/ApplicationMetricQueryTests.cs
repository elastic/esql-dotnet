// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Integration.Tests;

namespace Elastic.Integration.Tests.Esql;

/// <summary>ES|QL query tests for ApplicationMetrics comparing against LINQ to Objects.</summary>
public class ApplicationMetricQueryTests : IntegrationTestBase
{
	private const string DataStreamPattern = "metrics-ecommerce.app-production*";

	[Test]
	public async Task Metrics_CountMatches()
	{
		var esqlCount = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.CountAsync();

		var linqCount = TestData.Metrics.Count;

		esqlCount.Should().Be(linqCount);
	}

	[Test]
	public async Task Metrics_FilterByMetricSetName_SystemCountMatches()
	{
		const string metricSet = "system";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == metricSet)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Metrics
			.Where(m => m.MetricSetName == metricSet)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Metrics_FilterByMetricSetName_AppCountMatches()
	{
		const string metricSet = "app";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == metricSet)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Metrics
			.Where(m => m.MetricSetName == metricSet)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Metrics_FilterByMetricSetName_BusinessCountMatches()
	{
		const string metricSet = "business";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == metricSet)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Metrics
			.Where(m => m.MetricSetName == metricSet)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Metrics_FilterByMetricSetName_DatabaseCountMatches()
	{
		const string metricSet = "database";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == metricSet)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Metrics
			.Where(m => m.MetricSetName == metricSet)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Metrics_FilterByServiceName_CountMatches()
	{
		const string serviceName = "api-gateway";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.ServiceName == serviceName)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Metrics
			.Where(m => m.ServiceName == serviceName)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Metrics_FilterByHostName_CountMatches()
	{
		const string hostName = "web-01";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.HostName == hostName)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Metrics
			.Where(m => m.HostName == hostName)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Metrics_FilterSystemMetricsFromService_CountMatches()
	{
		const string serviceName = "order-service";
		const string metricSet = "system";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.ServiceName == serviceName && m.MetricSetName == metricSet)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Metrics
			.Where(m => m.ServiceName == serviceName && m.MetricSetName == metricSet)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Metrics_OrderByTimestamp_MostRecent10()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.OrderByDescending(m => m.Timestamp)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Metrics
			.OrderByDescending(m => m.Timestamp)
			.Take(10)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Metrics_SelectSpecificFields()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == "system")
			.Take(5)
			.Select(m => new { m.Timestamp, m.ServiceName, m.HostName, m.CpuPercent, m.MemoryPercent })
			.AsEsql()
			.ToListAsync();

		esqlResults.Should().NotBeEmpty();
		esqlResults.Should().HaveCountLessThanOrEqualTo(5);

		// Verify projected fields
		foreach (var result in esqlResults)
		{
			result.ServiceName.Should().NotBeNullOrEmpty();
			result.HostName.Should().NotBeNullOrEmpty();
		}
	}

	[Test]
	public async Task Metrics_MetricSetDistribution_CountsMatch()
	{
		var systemCount = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == "system")
			.AsEsql()
			.CountAsync();

		var appCount = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == "app")
			.AsEsql()
			.CountAsync();

		var businessCount = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == "business")
			.AsEsql()
			.CountAsync();

		var databaseCount = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == "database")
			.AsEsql()
			.CountAsync();

		var linqSystemCount = TestData.Metrics.Count(m => m.MetricSetName == "system");
		var linqAppCount = TestData.Metrics.Count(m => m.MetricSetName == "app");
		var linqBusinessCount = TestData.Metrics.Count(m => m.MetricSetName == "business");
		var linqDatabaseCount = TestData.Metrics.Count(m => m.MetricSetName == "database");

		systemCount.Should().Be(linqSystemCount);
		appCount.Should().Be(linqAppCount);
		businessCount.Should().Be(linqBusinessCount);
		databaseCount.Should().Be(linqDatabaseCount);
	}

	[Test]
	public async Task Metrics_ServiceDistribution_CountsMatch()
	{
		const string apiGateway = "api-gateway";
		const string orderService = "order-service";
		const string paymentService = "payment-service";

		var apiGatewayCount = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.ServiceName == apiGateway)
			.AsEsql()
			.CountAsync();

		var orderServiceCount = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.ServiceName == orderService)
			.AsEsql()
			.CountAsync();

		var paymentServiceCount = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.ServiceName == paymentService)
			.AsEsql()
			.CountAsync();

		var linqApiGatewayCount = TestData.Metrics.Count(m => m.ServiceName == apiGateway);
		var linqOrderServiceCount = TestData.Metrics.Count(m => m.ServiceName == orderService);
		var linqPaymentServiceCount = TestData.Metrics.Count(m => m.ServiceName == paymentService);

		apiGatewayCount.Should().Be(linqApiGatewayCount);
		orderServiceCount.Should().Be(linqOrderServiceCount);
		paymentServiceCount.Should().Be(linqPaymentServiceCount);
	}

	[Test]
	public async Task Metrics_AnySystemMetrics_ReturnsExpected()
	{
		var esqlAny = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == "system")
			.AsEsql()
			.AnyAsync();

		var linqAny = TestData.Metrics.Any(m => m.MetricSetName == "system");

		esqlAny.Should().Be(linqAny);
	}

	[Test]
	public async Task Metrics_FirstSystemMetric_ReturnsMetric()
	{
		var esqlFirst = await Fixture.EsqlClient
			.Query<ApplicationMetric>(DataStreamPattern)
			.Where(m => m.MetricSetName == "system")
			.AsEsql()
			.FirstAsync();

		esqlFirst.Should().NotBeNull();
		esqlFirst.MetricSetName.Should().Be("system");
	}
}
