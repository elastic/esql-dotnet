// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Usage;

/// <summary>
/// Real-world query examples demonstrating practical use cases.
/// </summary>
public class RealWorldExamplesTests : EsqlTestBase
{
	[Test]
	public void ErrorMonitoringDashboard()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.StatusCode >= 500)
			.Where(l => l.Level == "ERROR")
			.OrderByDescending(l => l.Timestamp)
			.Take(100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | WHERE log.level.keyword == "ERROR"
            | SORT @timestamp DESC
            | LIMIT 100
            """);
	}

	[Test]
	public void SlowRequestAnalysis()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Duration > 5000)
			.OrderByDescending(l => l.Duration)
			.Select(l => new { l.Message, l.Duration, l.Timestamp })
			.Take(50)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE duration > 5000
            | SORT duration DESC
            | KEEP message, duration
            | EVAL timestamp = @timestamp
            | LIMIT 50
            """);
	}

	[Test]
	public void FilteringByMultipleCriteria()
	{
		var esql = (
			from l in Client.Query<LogEntry>()
			where l.Level == "ERROR" || l.Level == "WARNING"
			where l.StatusCode >= 400
			orderby l.Timestamp descending
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (log.level.keyword == "ERROR" OR log.level.keyword == "WARNING")
            | WHERE statusCode >= 400
            | SORT @timestamp DESC
            """);
	}

	[Test]
	public void MetricsQuery()
	{
		var esql = Client.Query<MetricDocument>()
			.Where(m => m.Name == "cpu_usage")
			.Where(m => m.Value > 80)
			.OrderByDescending(m => m.Timestamp)
			.Take(100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM metrics-*
            | WHERE name.keyword == "cpu_usage"
            | WHERE value > 80
            | SORT timestamp DESC
            | LIMIT 100
            """);
	}
}
