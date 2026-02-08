// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.GroupBy;

public class MultipleFieldGroupByTests : EsqlTestBase
{
	[Test]
	public void GroupBy_MultipleFields_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => new { l.Level, l.StatusCode })
			.Select(g => new { g.Key.Level, g.Key.StatusCode, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY level = log.level, statusCode
            """);
	}

	[Test]
	public void GroupBy_MultipleFields_WithSum_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => new { l.Level, l.StatusCode })
			.Select(g => new { g.Key.Level, g.Key.StatusCode, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS totalDuration = SUM(duration) BY level = log.level, statusCode
            """);
	}

	[Test]
	public void GroupBy_MultipleFields_WithMultipleAggregations_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => new { l.Level, l.StatusCode })
			.Select(g => new
			{
				g.Key.Level,
				g.Key.StatusCode,
				Count = g.Count(),
				AvgDuration = g.Average(l => l.Duration)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*), avgDuration = AVG(duration) BY level = log.level, statusCode
            """);
	}
}
