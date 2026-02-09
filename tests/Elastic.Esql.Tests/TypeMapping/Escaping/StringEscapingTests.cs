// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.TypeMapping.Escaping;

public class StringEscapingTests : EsqlTestBase
{
	[Test]
	public void String_WithQuotes_EscapesCorrectly()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message == "He said \"hello\"")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "He said \"hello\""
            """);
	}

	[Test]
	public void String_WithBackslash_EscapesCorrectly()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message == "C:\\Users\\test")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "C:\\Users\\test"
            """);
	}

	[Test]
	public void String_WithNewline_EscapesCorrectly()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message == "line1\nline2")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "line1\nline2"
            """);
	}

	[Test]
	public void String_WithTab_EscapesCorrectly()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message == "col1\tcol2")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "col1\tcol2"
            """);
	}
}
