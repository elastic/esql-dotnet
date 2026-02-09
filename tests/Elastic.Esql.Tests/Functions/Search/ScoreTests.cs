// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Search;

public class ScoreTests : EsqlTestBase
{
	[Test]
	public void Score_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Relevance = EsqlFunctions.Score() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL relevance = SCORE()
            """);
	}

	[Test]
	public void Score_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Score() > 0.5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SCORE() > 0.5
            """);
	}
}
