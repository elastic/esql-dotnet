// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.PatternMatching;

public class MatchTests : EsqlTestBase
{
	[Test]
	public void Match_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Match(l.Message, "error"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE MATCH(message, "error")
            """);
	}

	[Test]
	public void Match_WithPhrase_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Match(l.Message, "connection timeout"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE MATCH(message, "connection timeout")
            """);
	}
}
