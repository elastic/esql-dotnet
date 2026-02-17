// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Usage;

/// <summary>
/// Tests for CreateQuery&lt;T&gt;().From(...) API with both fluent and query syntax.
/// </summary>
public class EsqlClientTests : EsqlTestBase
{
	[Test]
	public void Fluent_SimpleFilter()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            """.NativeLineEndings());
	}

	[Test]
	public void Fluent_FilterSortTake()
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
	public void Fluent_ExplicitIndexPattern()
	{
		var esql = CreateQuery<LogEntry>()
			.From("custom-index-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.Take(10)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM custom-index-*
            | WHERE log.level.keyword == "ERROR"
            | LIMIT 10
            """.NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_SimpleFilter()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("logs-*")
			where l.Level.MultiField("keyword") == "ERROR"
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            """.NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_CompleteQuery()
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
}
