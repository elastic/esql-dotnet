// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

// ToLower/ToUpper tests intentionally use == comparison pattern for LINQ expression tree translation
#pragma warning disable CA1862

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class StringMethodTests : EsqlTestBase
{
	[Test]
	public void Where_StringContains_GeneratesLike()
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

	[Test]
	public void Where_StringStartsWith_GeneratesLike()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").StartsWith("Error:"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword LIKE "Error:*"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_StringEndsWith_GeneratesLike()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").EndsWith("failed"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword LIKE "*failed"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_StringToLower_GeneratesToLower()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword").ToLowerInvariant() == "error")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE TO_LOWER(log.level.keyword) == "error"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_StringToUpper_GeneratesToUpper()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword").ToUpperInvariant() == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE TO_UPPER(log.level.keyword) == "ERROR"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_StringTrim_GeneratesTrim()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Trim() == "test")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE TRIM(message.keyword) == "test"
            """.NativeLineEndings());
	}

	[Test]
	public void Contains_EscapesTheWildcardsOfThePattern()
	{
		// "*" and "?" are wildcards to LIKE, so a value holding them is escaped for the
		// pattern, and the pattern's backslash is escaped again for the string literal
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.Contains("a*b?c"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message LIKE "*a\\*b\\?c*"
            """.NativeLineEndings());
	}

	[Test]
	public void StartsWith_EscapesABackslashTwice()
	{
		// a backslash in the value is escaped once for the pattern and once for the literal
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.StartsWith("C:\\"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message LIKE "C:\\\\*"
            """.NativeLineEndings());
	}

	[Test]
	public void EndsWith_EscapesAQuoteForTheLiteralOnly()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.EndsWith("say \"hi\""))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message LIKE "*say \"hi\""
            """.NativeLineEndings());
	}
}
