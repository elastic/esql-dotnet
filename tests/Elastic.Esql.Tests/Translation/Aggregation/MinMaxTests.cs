// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class MinMaxTests : EsqlTestBase
{
	[Test]
	public void Min_Field_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, MinDuration = g.Min(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS minDuration = MIN(duration) BY log.level
            """);
	}

	[Test]
	public void Max_Field_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, MaxDuration = g.Max(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS maxDuration = MAX(duration) BY log.level
            """);
	}

	[Test]
	public void MinMax_Combined_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				MinDuration = g.Min(l => l.Duration),
				MaxDuration = g.Max(l => l.Duration)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS minDuration = MIN(duration), maxDuration = MAX(duration) BY log.level
            """);
	}

	[Test]
	public void Min_WithFilter_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.StatusCode >= 400)
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, MinDuration = g.Min(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 400
            | STATS minDuration = MIN(duration) BY log.level
            """);
	}

	[Test]
	public void Max_IntegerField_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, MaxStatusCode = g.Max(l => l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS maxStatusCode = MAX(statusCode) BY log.level
            """);
	}
}
