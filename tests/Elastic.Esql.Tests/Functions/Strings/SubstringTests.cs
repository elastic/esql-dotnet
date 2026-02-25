// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class SubstringTests : EsqlTestBase
{
	[Test]

	public void Substring_TwoArgs_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Prefix = EsqlFunctions.Substring(l.Message, 0) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL prefix = SUBSTRING(message, 0)
            | KEEP prefix
            """.NativeLineEndings());
	}

	[Test]

	public void Substring_ThreeArgs_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Prefix = EsqlFunctions.Substring(l.Message, 0, 10) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL prefix = SUBSTRING(message, 0, 10)
            | KEEP prefix
            """.NativeLineEndings());
	}
}
