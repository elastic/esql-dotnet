// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class EncodingTests : EsqlTestBase
{
	[Test]
	public void BitLength_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.BitLength(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = BIT_LENGTH(message)
            """);
	}

	[Test]
	public void ByteLength_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.ByteLength(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = BYTE_LENGTH(message)
            """);
	}

	[Test]
	public void FromBase64_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.FromBase64(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = FROM_BASE64(message)
            """);
	}

	[Test]
	public void ToBase64_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.ToBase64(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = TO_BASE64(message)
            """);
	}

	[Test]
	public void UrlEncode_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.UrlEncode(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = URL_ENCODE(message)
            """);
	}

	[Test]
	public void UrlEncodeComponent_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.UrlEncodeComponent(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = URL_ENCODE_COMPONENT(message)
            """);
	}

	[Test]
	public void UrlDecode_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.UrlDecode(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = URL_DECODE(message)
            """);
	}

	[Test]
	public void Chunk_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Chunk(l.Message, 100) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = CHUNK(message, 100)
            """);
	}

	[Test]
	public void StringLength_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Len = l.Message.Length })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL len = LENGTH(message)
            """);
	}

	[Test]
	public void StringLength_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message.Length > 100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE LENGTH(message.keyword) > 100
            """);
	}
}
