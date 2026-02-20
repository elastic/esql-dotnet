// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class BooleanOperatorTests : EsqlTestBase
{
	[Test]
	public void Where_AndOperator_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR" && l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (log.level.keyword == "ERROR" AND statusCode >= 500)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_OrOperator_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR" || l.Level.MultiField("keyword") == "CRITICAL")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (log.level.keyword == "ERROR" OR log.level.keyword == "CRITICAL")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_NotOperator_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => !(l.Level.MultiField("keyword") == "DEBUG"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE NOT log.level.keyword == "DEBUG"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ComplexBooleanExpression_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => (l.Level.MultiField("keyword") == "ERROR" || l.Level.MultiField("keyword") == "CRITICAL") && l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE ((log.level.keyword == "ERROR" OR log.level.keyword == "CRITICAL") AND statusCode >= 500)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_BooleanField_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.IsError)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE isError
            """.NativeLineEndings());
	}

	[Test]
	public void Where_NegatedBooleanField_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => !l.IsError)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE NOT isError
            """.NativeLineEndings());
	}
}
