// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Search;

public class MatchPhraseTests : EsqlTestBase
{
	[Test]
	public void MatchPhrase_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.MatchPhrase(l.Message, "connection timeout"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE MATCH_PHRASE(message, "connection timeout")
            """);
	}
}
