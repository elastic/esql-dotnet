// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Usage;

/// <summary>
/// Tests for CreateQuery&lt;T&gt;().From(...) API with LINQ query syntax (from...where...select).
/// </summary>
public class QuerySyntaxTests : EsqlTestBase
{
	[Test]
	public void SimpleFilter()
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
	public void FilterWithOrderBy()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("logs-*")
			where l.Level.MultiField("keyword") == "ERROR"
			orderby l.Timestamp descending
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            | SORT @timestamp DESC
            """.NativeLineEndings());
	}

	[Test]
	public void MultipleWhereClause()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("logs-*")
			where l.Level.MultiField("keyword") == "ERROR"
			where l.Duration > 1000
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            | WHERE duration > 1000
            """.NativeLineEndings());
	}

	[Test]
	public void Projection()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("logs-*")
			where l.StatusCode >= 500
			select new { l.Message, l.Duration }
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | KEEP message, duration
            """.NativeLineEndings());
	}

	[Test]
	public void ExplicitIndexPattern()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("app-logs-*")
			where l.Level.MultiField("keyword") == "WARNING"
			orderby l.Timestamp descending
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM app-logs-*
            | WHERE log.level.keyword == "WARNING"
            | SORT @timestamp DESC
            """.NativeLineEndings());
	}
}
