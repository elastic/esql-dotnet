// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.TypeMapping.Escaping;

public class StringEscapingTests : EsqlTestBase
{
	[Test]
	public void String_WithQuotes_EscapesCorrectly()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword") == "He said \"hello\"")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "He said \"hello\""
            """.NativeLineEndings());
	}

	[Test]
	public void String_WithBackslash_EscapesCorrectly()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword") == "C:\\Users\\test")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "C:\\Users\\test"
            """.NativeLineEndings());
	}

	[Test]
	public void String_WithNewline_EscapesCorrectly()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword") == "line1\nline2")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "line1\nline2"
            """.NativeLineEndings());
	}

	[Test]
	public void String_WithTab_EscapesCorrectly()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword") == "col1\tcol2")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "col1\tcol2"
            """.NativeLineEndings());
	}
}
