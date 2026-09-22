// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class StringIndexTests : EsqlTestBase
{
	[Test]
	public void String_Substring_StartIndex_InSelect_GeneratesSubstring()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Sub = l.Message.Substring(5) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL sub = SUBSTRING(message, 6)
            | KEEP sub
            """.NativeLineEndings());
	}

	[Test]
	public void String_Substring_MaxValueStartIndex_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Sub = l.Message.Substring(int.MaxValue) })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*int.MaxValue*");
	}

	[Test]
	public void String_Indexer_CapturedMaxValueIndex_ThrowsNotSupported()
	{
		var index = int.MaxValue;

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Ch = l.Message[index] })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*int.MaxValue*");
	}

	[Test]
	public void String_Substring_CapturedNegativeStartIndex_ThrowsNotSupported()
	{
		var start = -1;

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Sub = l.Message.Substring(start) })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*-1*non-negative*");
	}

	[Test]
	public void String_Substring_StartAndLength_InSelect_GeneratesSubstring()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Sub = l.Message.Substring(0, 10) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL sub = SUBSTRING(message, 1, 10)
            | KEEP sub
            """.NativeLineEndings());
	}

	[Test]
	public void String_Index_FirstChar_InSelect_GeneratesSubstring()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { FirstChar = l.Message[0] })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL firstChar = SUBSTRING(message, 1, 1)
            | KEEP firstChar
            """.NativeLineEndings());
	}

	[Test]
	public void String_Index_AtPosition_InSelect_GeneratesSubstring()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { CharAt5 = l.Message[5] })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL charAt5 = SUBSTRING(message, 6, 1)
            | KEEP charAt5
            """.NativeLineEndings());
	}

	[Test]
	public void String_Index_FirstChar_InWhere_GeneratesSubstring()
	{
		// Note: ES|QL SUBSTRING returns a string, so we compare with string
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Substring(0, 1) == "E")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 1, 1) == "E"
            """.NativeLineEndings());
	}

	[Test]
	public void String_Index_AtPosition_InWhere_GeneratesSubstring()
	{
		// Note: ES|QL SUBSTRING returns a string, so we compare with string
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Substring(3, 1) == "O")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 4, 1) == "O"
            """.NativeLineEndings());
	}

	[Test]
	public void String_Substring_InWhere_GeneratesSubstring()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Substring(0, 5) == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 1, 5) == "ERROR"
            """.NativeLineEndings());
	}

	[Test]
	public void String_Substring_WithLength_InWhere_GeneratesSubstring()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Substring(0, 4) == "INFO")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 1, 4) == "INFO"
            """.NativeLineEndings());
	}

	[Test]
	public void String_Substring_CapturedStartIndex_InWhere_FoldsOneBasedStart()
	{
		var start = 3;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Substring(start, 1) == "O")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 4, 1) == "O"
            """.NativeLineEndings());
	}

	[Test]
	public void String_Substring_FieldStartIndex_InWhere_EmitsShiftedExpression()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Substring(l.StatusCode, 1) == "O")
			.ToString();

		_ = esql.Should().Contain("SUBSTRING(message.keyword, (statusCode) + 1, 1)");
	}

	[Test]
	public void String_Substring_CapturedStartIndex_Parameterized_FoldsToLiteral()
	{
		var start = 3;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Substring(start, 1) == "O")
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Contain("SUBSTRING(message.keyword, 4, 1)");
	}
}
