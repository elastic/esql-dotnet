// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class LeftRightTests : EsqlTestBase
{
	[Test]
	public void Left_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = EsqlFunctions.Left(l.Message, 5) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = LEFT(message, 5)
            """.NativeLineEndings());
	}

	[Test]
	public void Right_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = EsqlFunctions.Right(l.Message, 5) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = RIGHT(message, 5)
            """.NativeLineEndings());
	}

	[Test]
	public void Left_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Left(l.Message.MultiField("keyword"), 3) == "ERR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE LEFT(message.keyword, 3) == "ERR"
            """.NativeLineEndings());
	}
}
