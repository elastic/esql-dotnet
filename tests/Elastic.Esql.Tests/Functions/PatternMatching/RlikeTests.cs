// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.PatternMatching;

public class RlikeTests : EsqlTestBase
{
	[Test]
	public void Rlike_SimplePattern_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Rlike(l.Message.MultiField("keyword"), "error|warning"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword RLIKE "error|warning"
            """.NativeLineEndings());
	}

	[Test]
	public void Rlike_ComplexPattern_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Rlike(l.Message.MultiField("keyword"), "^[A-Z]{3}-\\d{4}"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword RLIKE "^[A-Z]{3}-\\d{4}"
            """.NativeLineEndings());
	}
}
