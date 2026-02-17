// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Usage;

/// <summary>
/// Overview of ES|QL query entry points with comprehensive examples.
/// Each test demonstrates one entry point with a realistic, full-featured query.
/// </summary>
public class UsageOverviewTests : EsqlTestBase
{
	[Test]
	public void Client_Fluent_ComprehensiveExample()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
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
            """.NativeLineEndings());
	}

	[Test]
	public void Client_QuerySyntax_ComprehensiveExample()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("logs-*")
			where l.Level.MultiField("keyword") == "ERROR" || l.Level.MultiField("keyword") == "WARNING"
			where l.StatusCode >= 400
			orderby l.Timestamp descending
			select new { l.Message, l.Duration }
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (log.level.keyword == "ERROR" OR log.level.keyword == "WARNING")
            | WHERE statusCode >= 400
            | SORT @timestamp DESC
            | KEEP message, duration
            """.NativeLineEndings());
	}

	[Test]
	public void Client_Fluent_WithExplicitIndex()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.OrderByDescending(l => l.Timestamp)
			.Take(50)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | SORT @timestamp DESC
            | LIMIT 50
            """.NativeLineEndings());
	}

	[Test]
	public void Client_QuerySyntax_WithProjection()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("logs-*")
			where l.Level.MultiField("keyword") == "ERROR"
			where l.Duration > 500
			orderby l.Timestamp descending
			select new { l.Message, l.Duration }
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            | WHERE duration > 500
            | SORT @timestamp DESC
            | KEEP message, duration
            """.NativeLineEndings());
	}

	[Test]
	public void ExplicitIndexPattern_ComprehensiveExample()
	{
		var esql = CreateQuery<LogEntry>()
			.From("production-logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.OrderByDescending(l => l.Timestamp)
			.Take(100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM production-logs-*
            | WHERE log.level.keyword == "ERROR"
            | SORT @timestamp DESC
            | LIMIT 100
            """.NativeLineEndings());
	}
}
