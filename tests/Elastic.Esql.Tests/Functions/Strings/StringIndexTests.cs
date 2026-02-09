// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class StringIndexTests : EsqlTestBase
{
	[Test]
	public void String_Substring_StartIndex_InSelect_GeneratesSubstring()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Sub = l.Message.Substring(5) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL sub = SUBSTRING(message, 5)
            """);
	}

	[Test]
	public void String_Substring_StartAndLength_InSelect_GeneratesSubstring()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Sub = l.Message.Substring(0, 10) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL sub = SUBSTRING(message, 0, 10)
            """);
	}

	[Test]
	public void String_Index_FirstChar_InSelect_GeneratesSubstring()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { FirstChar = l.Message[0] })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL firstChar = SUBSTRING(message, 1, 1)
            """);
	}

	[Test]
	public void String_Index_AtPosition_InSelect_GeneratesSubstring()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { CharAt5 = l.Message[5] })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL charAt5 = SUBSTRING(message, 6, 1)
            """);
	}

	[Test]
	public void String_Index_FirstChar_InWhere_GeneratesSubstring()
	{
		// Note: ES|QL SUBSTRING returns a string, so we compare with string
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.Substring(0, 1) == "E")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 0, 1) == "E"
            """);
	}

	[Test]
	public void String_Index_AtPosition_InWhere_GeneratesSubstring()
	{
		// Note: ES|QL SUBSTRING returns a string, so we compare with string
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.Substring(3, 1) == "O")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 3, 1) == "O"
            """);
	}

	[Test]
	public void String_Substring_InWhere_GeneratesSubstring()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.Substring(0, 5) == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 0, 5) == "ERROR"
            """);
	}

	[Test]
	public void String_Substring_WithLength_InWhere_GeneratesSubstring()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.Substring(0, 4) == "INFO")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE SUBSTRING(message.keyword, 0, 4) == "INFO"
            """);
	}
}
