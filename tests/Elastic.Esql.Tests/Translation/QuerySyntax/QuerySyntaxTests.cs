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
	public void QuerySyntax_WhereOrderBy_GeneratesCorrectEsql()
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
	public void QuerySyntax_WhereSelectProjection_GeneratesCorrectEsql()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("logs-*")
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
            """.NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_MultipleWhere_GeneratesCorrectEsql()
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
	public void QuerySyntax_OrderByThenBy_GeneratesCorrectEsql()
	{
		var esql = (
			from l in CreateQuery<LogEntry>()
				.From("logs-*")
			orderby l.Level.MultiField("keyword"), l.Timestamp descending
			select l
		).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT log.level.keyword
            | SORT @timestamp DESC
            """.NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_CompleteQuery_GeneratesCorrectEsql()
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
