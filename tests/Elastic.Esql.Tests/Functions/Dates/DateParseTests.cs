// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class DateParseTests : EsqlTestBase
{
	[Test]
	public void DateParse_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Parsed = EsqlFunctions.DateParse("yyyy-MM-dd", l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL parsed = DATE_PARSE("yyyy-MM-dd", message)
            | KEEP parsed
            """.NativeLineEndings());
	}
}
