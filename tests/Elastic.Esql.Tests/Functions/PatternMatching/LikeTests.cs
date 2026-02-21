// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.PatternMatching;

public class LikeTests : EsqlTestBase
{
	[Test]
	public void Like_StartsWithPattern_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Like(l.Message.MultiField("keyword"), "ERROR*"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword LIKE "ERROR*"
            """.NativeLineEndings());
	}

	[Test]
	public void Like_EndsWithPattern_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Like(l.Message.MultiField("keyword"), "*failed"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword LIKE "*failed"
            """.NativeLineEndings());
	}

	[Test]
	public void Like_ContainsPattern_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Like(l.Message.MultiField("keyword"), "*timeout*"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword LIKE "*timeout*"
            """.NativeLineEndings());
	}
}
