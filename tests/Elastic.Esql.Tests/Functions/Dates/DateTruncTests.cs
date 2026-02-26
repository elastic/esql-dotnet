// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class DateTruncTests : EsqlTestBase
{
	[Test]

	public void DateTrunc_Hour_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Hour = EsqlFunctions.DateTrunc("hour", l.Timestamp) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL hour = DATE_TRUNC("hour", @timestamp)
            | KEEP hour
            """.NativeLineEndings());
	}

	[Test]

	public void DateTrunc_Day_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Day = EsqlFunctions.DateTrunc("day", l.Timestamp) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL day = DATE_TRUNC("day", @timestamp)
            | KEEP day
            """.NativeLineEndings());
	}
}
