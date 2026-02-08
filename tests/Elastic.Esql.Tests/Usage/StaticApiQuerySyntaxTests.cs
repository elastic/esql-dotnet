// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Usage;

/// <summary>
/// Tests for Client.Query&lt;T&gt;() API with LINQ query syntax (from...where...select).
/// </summary>
public class QuerySyntaxTests : EsqlTestBase
{
	[Test]
	public void SimpleFilter()
	{
		var esql = (
			from l in Client.Query<LogEntry>()
			where l.Level == "ERROR"
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            """);
	}

	[Test]
	public void FilterWithOrderBy()
	{
		var esql = (
			from l in Client.Query<LogEntry>()
			where l.Level == "ERROR"
			orderby l.Timestamp descending
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            | SORT @timestamp DESC
            """);
	}

	[Test]
	public void MultipleWhereClause()
	{
		var esql = (
			from l in Client.Query<LogEntry>()
			where l.Level == "ERROR"
			where l.Duration > 1000
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            | WHERE duration > 1000
            """);
	}

	[Test]
	public void Projection()
	{
		var esql = (
			from l in Client.Query<LogEntry>()
			where l.StatusCode >= 500
			select new { l.Message, l.Duration }
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | KEEP message, duration
            """);
	}

	[Test]
	public void ExplicitIndexPattern()
	{
		var esql = (
			from l in Client.Query<LogEntry>("app-logs-*")
			where l.Level == "WARNING"
			orderby l.Timestamp descending
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM app-logs-*
            | WHERE log.level == "WARNING"
            | SORT @timestamp DESC
            """);
	}
}
