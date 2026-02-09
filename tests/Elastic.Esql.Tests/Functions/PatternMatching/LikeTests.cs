// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.PatternMatching;

public class LikeTests : EsqlTestBase
{
	[Test]
	public void Like_StartsWithPattern_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Like(l.Message, "ERROR*"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword LIKE "ERROR*"
            """);
	}

	[Test]
	public void Like_EndsWithPattern_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Like(l.Message, "*failed"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword LIKE "*failed"
            """);
	}

	[Test]
	public void Like_ContainsPattern_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Like(l.Message, "*timeout*"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword LIKE "*timeout*"
            """);
	}
}
