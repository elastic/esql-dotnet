// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.PatternMatching;

public class RlikeTests : EsqlTestBase
{
	[Test]
	public void Rlike_SimplePattern_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Rlike(l.Message, "error|warning"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message RLIKE "error|warning"
            """);
	}

	[Test]
	public void Rlike_ComplexPattern_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Rlike(l.Message, "^[A-Z]{3}-\\d{4}"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message RLIKE "^[A-Z]{3}-\\d{4}"
            """);
	}
}
