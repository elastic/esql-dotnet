// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class LengthTests : EsqlTestBase
{
	[Test]
	public void Length_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Length(l.Message) > 100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE LENGTH(message.keyword) > 100
            """);
	}

	[Test]

	public void Length_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { l.Message, MessageLength = EsqlFunctions.Length(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | EVAL messageLength = LENGTH(message)
            """);
	}
}
