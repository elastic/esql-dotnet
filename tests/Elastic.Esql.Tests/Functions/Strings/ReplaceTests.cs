// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class ReplaceTests : EsqlTestBase
{
	[Test]
	public void Replace_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = EsqlFunctions.Replace(l.Message, "old", "new") })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = REPLACE(message, "old", "new")
            """.NativeLineEndings());
	}

	[Test]
	public void Replace_Native_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = l.Message.Replace("old", "new") })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = REPLACE(message, "old", "new")
            """.NativeLineEndings());
	}

	[Test]
	public void Replace_Native_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Replace("old", "new") == "updated")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE REPLACE(message.keyword, "old", "new") == "updated"
            """.NativeLineEndings());
	}
}
