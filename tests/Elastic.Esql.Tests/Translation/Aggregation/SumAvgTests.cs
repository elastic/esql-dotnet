// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class SumAvgTests : EsqlTestBase
{
	[Test]
	public void Sum_Field_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS totalDuration = SUM(duration) BY level = log.level.keyword
            """);
	}

	[Test]
	public void Average_Field_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, AvgDuration = g.Average(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS avgDuration = AVG(duration) BY level = log.level.keyword
            """);
	}

	[Test]
	public void Sum_WithFilter_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.StatusCode >= 400)
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 400
            | STATS totalDuration = SUM(duration) BY level = log.level.keyword
            """);
	}

	[Test]
	public void Average_WithFilter_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, AvgDuration = g.Average(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            | STATS avgDuration = AVG(duration) BY level = log.level.keyword
            """);
	}

	[Test]
	public void Sum_IntegerField_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, TotalStatusCodes = g.Sum(l => l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS totalStatusCodes = SUM(statusCode) BY level = log.level.keyword
            """);
	}
}
