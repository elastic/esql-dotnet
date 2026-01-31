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
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.Contains("error"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message LIKE "*error*"
            """);
	}

	[Test]
	public void Where_StringStartsWith_GeneratesLike()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.StartsWith("Error:"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message LIKE "Error:*"
            """);
	}

	[Test]
	public void Where_StringEndsWith_GeneratesLike()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.EndsWith("failed"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message LIKE "*failed"
            """);
	}

	[Test]
	public void Where_StringToLower_GeneratesToLower()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level.ToLowerInvariant() == "error")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE TO_LOWER(log.level) == "error"
            """);
	}

	[Test]
	public void Where_StringToUpper_GeneratesToUpper()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level.ToUpperInvariant() == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE TO_UPPER(log.level) == "ERROR"
            """);
	}

	[Test]
	public void Where_StringTrim_GeneratesTrim()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.Trim() == "test")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE TRIM(message) == "test"
            """);
	}
}
