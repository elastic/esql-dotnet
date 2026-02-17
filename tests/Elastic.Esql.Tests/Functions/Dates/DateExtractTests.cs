// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class DateExtractTests : EsqlTestBase
{
	[Test]
	public void DateTime_Year_InWhere_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.Year == 2024)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("year", @timestamp) == 2024
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_Month_InWhere_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.Month == 12)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("month", @timestamp) == 12
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_Day_InWhere_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.Day == 25)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("day_of_month", @timestamp) == 25
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_Hour_InWhere_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.Hour >= 9 && l.Timestamp.Hour <= 17)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (DATE_EXTRACT("hour", @timestamp) >= 9 AND DATE_EXTRACT("hour", @timestamp) <= 17)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_Minute_InWhere_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.Minute == 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("minute", @timestamp) == 0
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_Second_InWhere_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.Second < 30)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("second", @timestamp) < 30
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_DayOfWeek_InWhere_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.DayOfWeek == DayOfWeek.Monday)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("day_of_week", @timestamp) == 1
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_DayOfYear_InWhere_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.DayOfYear == 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("day_of_year", @timestamp) == 1
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_Year_InSelect_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Timestamp.Year })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL year = DATE_EXTRACT("year", @timestamp)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_MultipleProperties_InSelect_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Timestamp.Year, l.Timestamp.Month, l.Timestamp.Day })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL year = DATE_EXTRACT("year", @timestamp), month = DATE_EXTRACT("month", @timestamp), day = DATE_EXTRACT("day_of_month", @timestamp)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_Hour_InSelect_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Timestamp.Hour })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL hour = DATE_EXTRACT("hour", @timestamp)
            """.NativeLineEndings());
	}
}
