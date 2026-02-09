// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>
/// Tests that text fields automatically get .keyword suffix for exact-match operations,
/// while MATCH uses the base text field name.
/// </summary>
public class TextKeywordFieldTests : EsqlTestBase
{
	[Test]
	public void TextField_Equality_AppendsKeyword()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message == "test")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword == "test"
			""");
	}

	[Test]
	public void TextField_Inequality_AppendsKeyword()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message != "test")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword != "test"
			""");
	}

	[Test]
	public void TextField_Like_AppendsKeyword()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Like(l.Message, "*error*"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword LIKE "*error*"
			""");
	}

	[Test]
	public void TextField_Match_UsesBaseFieldName()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Match(l.Message, "error"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE MATCH(message, "error")
			""");
	}

	[Test]
	public void TextField_OrderBy_AppendsKeyword()
	{
		var esql = Client.Query<LogEntry>()
			.OrderBy(l => l.Message)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| SORT message.keyword
			""");
	}

	[Test]
	public void TextField_GroupBy_AppendsKeyword()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Message)
			.Select(g => new { Message = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS count = COUNT(*) BY message = message.keyword
			""");
	}

	[Test]
	public void TextField_Projection_UsesBaseFieldName()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""");
	}

	[Test]
	public void TextField_StringContains_AppendsKeyword()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.Contains("error"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword LIKE "*error*"
			""");
	}

	[Test]
	public void NonStringField_NoKeywordSuffix()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= 500
			""");
	}

	[Test]
	public void TextFieldWithJsonPropertyName_AppendsKeyword()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword == "ERROR"
			""");
	}
}
