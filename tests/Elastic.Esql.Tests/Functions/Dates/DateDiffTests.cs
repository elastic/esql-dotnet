// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class DateDiffTests : EsqlTestBase
{
	[Test]
	public void DateDiff_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Diff = EsqlFunctions.DateDiff("day", l.Timestamp, EsqlFunctions.Now()) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL diff = DATE_DIFF("day", @timestamp, NOW())
            """);
	}

	[Test]
	public void DateDiff_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.DateDiff("hour", l.Timestamp, EsqlFunctions.Now()) < 24)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_DIFF("hour", @timestamp, NOW()) < 24
            """);
	}
}
