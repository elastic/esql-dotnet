// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>
/// Tests that text fields use .MultiField("keyword") for exact-match operations,
/// while MATCH uses the base text field name.
/// </summary>
public class TextKeywordFieldTests : EsqlTestBase
{
	[Test]
	public void TextField_Equality_AppendsKeyword()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword") == "test")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword == "test"
			""".NativeLineEndings());
	}

	[Test]
	public void TextField_Inequality_AppendsKeyword()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword") != "test")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword != "test"
			""".NativeLineEndings());
	}

	[Test]
	public void TextField_Like_AppendsKeyword()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Like(l.Message.MultiField("keyword"), "*error*"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword LIKE "*error*"
			""".NativeLineEndings());
	}

	[Test]
	public void TextField_Match_UsesBaseFieldName()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Match(l.Message, "error"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE MATCH(message, "error")
			""".NativeLineEndings());
	}

	[Test]
	public void TextField_OrderBy_AppendsKeyword()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Message.MultiField("keyword"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| SORT message.keyword
			""".NativeLineEndings());
	}

	[Test]
	public void TextField_GroupBy_AppendsKeyword()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Message.MultiField("keyword"))
			.Select(g => new { Message = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS count = COUNT(*) BY message = message.keyword
			""".NativeLineEndings());
	}

	[Test]
	public void TextField_Projection_UsesBaseFieldName()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void TextField_StringContains_AppendsKeyword()
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
	public void NonStringField_NoKeywordSuffix()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= 500
			""".NativeLineEndings());
	}

	[Test]
	public void TextFieldWithJsonPropertyName_AppendsKeyword()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword == "ERROR"
			""".NativeLineEndings());
	}
}
