// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class LtrimRtrimTests : EsqlTestBase
{
	[Test]
	public void Ltrim_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = EsqlFunctions.Ltrim(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = LTRIM(message)
            """.NativeLineEndings());
	}

	[Test]
	public void Rtrim_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = EsqlFunctions.Rtrim(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = RTRIM(message)
            """.NativeLineEndings());
	}

	[Test]
	public void TrimStart_Native_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = l.Message.TrimStart() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = LTRIM(message)
            """.NativeLineEndings());
	}

	[Test]
	public void TrimEnd_Native_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = l.Message.TrimEnd() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = RTRIM(message)
            """.NativeLineEndings());
	}

	[Test]
	public void TrimStart_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").TrimStart() == "hello")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE LTRIM(message.keyword) == "hello"
            """.NativeLineEndings());
	}

	[Test]
	public void TrimEnd_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").TrimEnd() == "hello")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE RTRIM(message.keyword) == "hello"
            """.NativeLineEndings());
	}
}
