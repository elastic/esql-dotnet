// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class EqualityOperatorTests : EsqlTestBase
{
	[Test]
	public void Where_EqualityOperator_GeneratesCorrectEsql()
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
	public void Where_NotEqualOperator_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") != "DEBUG")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword != "DEBUG"
            """.NativeLineEndings());
	}
}
