// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class GlobalAggregationTests : EsqlTestBase
{
	[Test]
	public void GlobalCount_WithConstantGroupBy_GeneratesStats()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => 1)
			.Select(g => new { Total = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS total = COUNT(*)
            """);
	}

	[Test]
	public void GlobalSum_WithConstantGroupBy_GeneratesStats()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => 1)
			.Select(g => new { TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS totalDuration = SUM(duration)
            """);
	}

	[Test]
	public void GlobalAverage_WithConstantGroupBy_GeneratesStats()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => 1)
			.Select(g => new { AvgDuration = g.Average(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS avgDuration = AVG(duration)
            """);
	}

	[Test]
	public void GlobalMinMax_WithConstantGroupBy_GeneratesStats()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => 1)
			.Select(g => new
			{
				MinDuration = g.Min(l => l.Duration),
				MaxDuration = g.Max(l => l.Duration)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS minDuration = MIN(duration), maxDuration = MAX(duration)
            """);
	}

	[Test]
	public void GlobalMultipleAggregations_WithConstantGroupBy_GeneratesStats()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => 1)
			.Select(g => new
			{
				Count = g.Count(),
				Sum = g.Sum(l => l.Duration),
				Avg = g.Average(l => l.Duration),
				Min = g.Min(l => l.Duration),
				Max = g.Max(l => l.Duration)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*), sum = SUM(duration), avg = AVG(duration), min = MIN(duration), max = MAX(duration)
            """);
	}

	[Test]
	public void GlobalAggregation_WithFilter_GeneratesStats()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.GroupBy(l => 1)
			.Select(g => new { ErrorCount = g.Count(), AvgDuration = g.Average(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            | STATS errorCount = COUNT(*), avgDuration = AVG(duration)
            """);
	}
}
