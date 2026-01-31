// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.QuerySyntax;

public class QuerySyntaxTests : EsqlTestBase
{
	[Test]
	public void QuerySyntax_Where_GeneratesCorrectEsql()
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
	public void QuerySyntax_WhereOrderBy_GeneratesCorrectEsql()
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
	public void QuerySyntax_WhereSelectProjection_GeneratesCorrectEsql()
	{
		var esql = (
			from l in Client.Query<LogEntry>()
			where l.StatusCode >= 500
			select new { l.Message, l.Timestamp }
		).ToString();

		// Timestamp uses @timestamp in ES|QL but result field is named 'timestamp'
		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | KEEP message
            | EVAL timestamp = @timestamp
            """);
	}

	[Test]
	public void QuerySyntax_MultipleWhere_GeneratesCorrectEsql()
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
	public void QuerySyntax_OrderByThenBy_GeneratesCorrectEsql()
	{
		var esql = (
			from l in Client.Query<LogEntry>()
			orderby l.Level, l.Timestamp descending
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT log.level
            | SORT @timestamp DESC
            """);
	}

	[Test]
	public void QuerySyntax_CompleteQuery_GeneratesCorrectEsql()
	{
		var esql = (
			from l in Client.Query<LogEntry>()
			where l.Level == "ERROR"
			where l.Duration > 500
			orderby l.Timestamp descending
			select new { l.Message, l.Duration }
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            | WHERE duration > 500
            | SORT @timestamp DESC
            | KEEP message, duration
            """);
	}
}
