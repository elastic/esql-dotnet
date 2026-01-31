// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class CountTests : EsqlTestBase
{
	[Test]
	public void Count_All_GeneratesCorrectEsql()
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
	public void Count_WithFilter_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.GroupBy(l => 1)
			.Select(g => new { ErrorCount = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            | STATS errorCount = COUNT(*)
            """);
	}

	[Test]
	public void Count_WithMultipleFilters_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR" && l.StatusCode >= 500)
			.GroupBy(l => 1)
			.Select(g => new { Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (log.level == "ERROR" AND statusCode >= 500)
            | STATS count = COUNT(*)
            """);
	}

	[Test]
	public void LongCount_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => 1)
			.Select(g => new { Total = g.LongCount() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS total = COUNT(*)
            """);
	}
}
