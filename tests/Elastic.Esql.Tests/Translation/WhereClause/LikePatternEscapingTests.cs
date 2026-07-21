// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class LikePatternEscapingTests : EsqlTestBase
{
	[Test]
	public void Where_StringContains_WithWildcard_EscapesPatternAndLiteral()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Contains("50% off*"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword LIKE "*50% off\\**"
			""".NativeLineEndings());
	}

	[Test]
	public void Where_StringStartsWith_WithBackslash_EscapesPatternAndLiteral()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").StartsWith(@"C:\logs"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword LIKE "C:\\\\logs*"
			""".NativeLineEndings());
	}

	[Test]
	public void Where_StringEndsWith_WithQuestionMark_EscapesPatternAndLiteral()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").EndsWith("done?"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword LIKE "*done\\?"
			""".NativeLineEndings());
	}

	[Test]
	public void Where_StringContains_PlainValue_IsUnchanged()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Contains("error"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword LIKE "*error*"
			""".NativeLineEndings());
	}
}
