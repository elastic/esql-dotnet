// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class LocateTests : EsqlTestBase
{
	[Test]
	public void Locate_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Pos = EsqlFunctions.Locate(l.Message, "error") })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL pos = LOCATE(message, "error")
            | KEEP pos
            """.NativeLineEndings());
	}

	[Test]
	public void Locate_WithStart_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Pos = EsqlFunctions.Locate(l.Message, "error", 5) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL pos = LOCATE(message, "error", 5)
            | KEEP pos
            """.NativeLineEndings());
	}

	[Test]
	public void IndexOf_Native_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Pos = l.Message.IndexOf("error") })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL pos = (LOCATE(message, "error") - 1)
            | KEEP pos
            """.NativeLineEndings());
	}

	[Test]
	public void IndexOf_Native_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").IndexOf("error") > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (LOCATE(message.keyword, "error") - 1) > 0
            """.NativeLineEndings());
	}

	[Test]
	public void IndexOf_Native_WithStartIndex_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Pos = l.Message.IndexOf("error", 5) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL pos = (LOCATE(message, "error", 6) - 1)
            | KEEP pos
            """.NativeLineEndings());
	}

	[Test]
	public void IndexOf_WithStringComparison_InWhere_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").IndexOf("error", StringComparison.OrdinalIgnoreCase) == 0)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*StringComparison*");
	}

	[Test]
	public void IndexOf_WithStringComparison_InSelect_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Pos = l.Message.IndexOf("error", StringComparison.OrdinalIgnoreCase) })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*StringComparison*");
	}
}
