// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class CountTests : EsqlTestBase
{
	[Test]
	public void Count_All_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => 1)
			.Select(g => new { Total = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS total = COUNT(*)
            """.NativeLineEndings());
	}

	[Test]
	public void Count_WithFilter_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.GroupBy(l => 1)
			.Select(g => new { ErrorCount = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            | STATS errorCount = COUNT(*)
            """.NativeLineEndings());
	}

	[Test]
	public void Count_WithMultipleFilters_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR" && l.StatusCode >= 500)
			.GroupBy(l => 1)
			.Select(g => new { Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (log.level.keyword == "ERROR" AND statusCode >= 500)
            | STATS count = COUNT(*)
            """.NativeLineEndings());
	}

	[Test]
	public void LongCount_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => 1)
			.Select(g => new { Total = g.LongCount() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS total = COUNT(*)
            """.NativeLineEndings());
	}
}
