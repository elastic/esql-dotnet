// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class SplitTests : EsqlTestBase
{
	[Test]
	public void Split_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Split(l.Message, ",") })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = SPLIT(message, ",")
            """);
	}

	[Test]
	public void Split_Native_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = l.Message.Split(",") })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = SPLIT(message, ",")
            """);
	}
}
