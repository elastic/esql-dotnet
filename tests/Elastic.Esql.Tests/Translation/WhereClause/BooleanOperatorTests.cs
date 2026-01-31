// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class BooleanOperatorTests : EsqlTestBase
{
	[Test]
	public void Where_AndOperator_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR" && l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (log.level == "ERROR" AND statusCode >= 500)
            """);
	}

	[Test]
	public void Where_OrOperator_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR" || l.Level == "CRITICAL")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (log.level == "ERROR" OR log.level == "CRITICAL")
            """);
	}

	[Test]
	public void Where_NotOperator_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => !(l.Level == "DEBUG"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE NOT log.level == "DEBUG"
            """);
	}

	[Test]
	public void Where_ComplexBooleanExpression_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => (l.Level == "ERROR" || l.Level == "CRITICAL") && l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE ((log.level == "ERROR" OR log.level == "CRITICAL") AND statusCode >= 500)
            """);
	}

	[Test]
	public void Where_BooleanField_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.IsError)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE isError
            """);
	}

	[Test]
	public void Where_NegatedBooleanField_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => !l.IsError)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE NOT isError
            """);
	}
}
