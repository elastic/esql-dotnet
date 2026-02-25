// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class DayMonthNameTests : EsqlTestBase
{
	[Test]
	public void DayName_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Day = EsqlFunctions.DayName(l.Timestamp) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL day = DAY_NAME(@timestamp)
            | KEEP day
            """.NativeLineEndings());
	}

	[Test]
	public void MonthName_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Month = EsqlFunctions.MonthName(l.Timestamp) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL month = MONTH_NAME(@timestamp)
            | KEEP month
            """.NativeLineEndings());
	}

	[Test]
	public void DayName_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.DayName(l.Timestamp) == "Monday")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DAY_NAME(@timestamp) == "Monday"
            """.NativeLineEndings());
	}
}
