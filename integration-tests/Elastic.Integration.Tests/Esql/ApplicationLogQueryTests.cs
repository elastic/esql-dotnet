// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Integration.Tests;

namespace Elastic.Integration.Tests.Esql;

/// <summary>ES|QL query tests for ApplicationLogs comparing against LINQ to Objects.</summary>
public class ApplicationLogQueryTests : IntegrationTestBase
{
	private const string DataStreamPattern = "logs-ecommerce.app-production*";

	[Test]
	public async Task Logs_CountMatches()
	{
		var esqlCount = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.CountAsync();

		var linqCount = TestData.Logs.Count;

		esqlCount.Should().Be(linqCount);
	}

	[Test]
	public async Task Logs_FilterByLevel_ErrorCountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Error)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Logs
			.Where(l => l.Level == LogLevel.Error)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Logs_FilterByLevel_WarnCountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Warn)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Logs
			.Where(l => l.Level == LogLevel.Warn)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Logs_FilterByLevel_InfoCountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Info)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Logs
			.Where(l => l.Level == LogLevel.Info)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Logs_FilterByServiceName_CountMatches()
	{
		const string serviceName = "api-gateway";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.ServiceName == serviceName)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Logs
			.Where(l => l.ServiceName == serviceName)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Logs_FilterByHttpMethod_CountMatches()
	{
		const string httpMethod = "POST";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.HttpMethod == httpMethod)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Logs
			.Where(l => l.HttpMethod == httpMethod)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Logs_FilterErrorsFromService_CountMatches()
	{
		const string serviceName = "order-service";

		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Error && l.ServiceName == serviceName)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Logs
			.Where(l => l.Level == LogLevel.Error && l.ServiceName == serviceName)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Logs_FilterByHttpStatusCode_CountMatches()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.HttpStatusCode == 500)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Logs
			.Where(l => l.HttpStatusCode == 500)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Logs_OrderByTimestamp_MostRecent10()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.OrderByDescending(l => l.Timestamp)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		var linqResults = TestData.Logs
			.OrderByDescending(l => l.Timestamp)
			.Take(10)
			.ToList();

		esqlResults.Should().HaveCount(linqResults.Count);
	}

	[Test]
	public async Task Logs_SelectSpecificFields()
	{
		var esqlResults = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Error)
			.Take(5)
			.Select(l => new { l.Timestamp, l.Level, l.ServiceName, l.Message })
			.AsEsql()
			.ToListAsync();

		esqlResults.Should().NotBeEmpty();
		esqlResults.Should().HaveCountLessThanOrEqualTo(5);

		// Verify projected fields
		foreach (var result in esqlResults)
		{
			result.ServiceName.Should().NotBeNullOrEmpty();
			result.Message.Should().NotBeNullOrEmpty();
			result.Level.Should().Be(LogLevel.Error);
		}
	}

	[Test]
	public async Task Logs_LevelDistribution_CountsMatch()
	{
		var errorCount = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Error)
			.AsEsql()
			.CountAsync();

		var warnCount = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Warn)
			.AsEsql()
			.CountAsync();

		var infoCount = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Info)
			.AsEsql()
			.CountAsync();

		var linqErrorCount = TestData.Logs.Count(l => l.Level == LogLevel.Error);
		var linqWarnCount = TestData.Logs.Count(l => l.Level == LogLevel.Warn);
		var linqInfoCount = TestData.Logs.Count(l => l.Level == LogLevel.Info);

		errorCount.Should().Be(linqErrorCount);
		warnCount.Should().Be(linqWarnCount);
		infoCount.Should().Be(linqInfoCount);
	}

	[Test]
	public async Task Logs_AnyErrors_ReturnsExpected()
	{
		var esqlAny = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Error)
			.AsEsql()
			.AnyAsync();

		var linqAny = TestData.Logs.Any(l => l.Level == LogLevel.Error);

		esqlAny.Should().Be(linqAny);
	}

	[Test]
	public async Task Logs_FirstError_ReturnsLog()
	{
		var esqlFirst = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.Level == LogLevel.Error)
			.AsEsql()
			.FirstAsync();

		esqlFirst.Should().NotBeNull();
		esqlFirst.Level.Should().Be(LogLevel.Error);
	}

	[Test]
	public async Task Logs_ServiceDistribution_CountsMatch()
	{
		const string apiGateway = "api-gateway";
		const string orderService = "order-service";

		var apiGatewayCount = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.ServiceName == apiGateway)
			.AsEsql()
			.CountAsync();

		var orderServiceCount = await Fixture.EsqlClient
			.Query<ApplicationLog>(DataStreamPattern)
			.Where(l => l.ServiceName == orderService)
			.AsEsql()
			.CountAsync();

		var linqApiGatewayCount = TestData.Logs.Count(l => l.ServiceName == apiGateway);
		var linqOrderServiceCount = TestData.Logs.Count(l => l.ServiceName == orderService);

		apiGatewayCount.Should().Be(linqApiGatewayCount);
		orderServiceCount.Should().Be(linqOrderServiceCount);
	}
}
