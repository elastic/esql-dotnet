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
            | WHERE DATE_EXTRACT("month_of_year", @timestamp) == 12
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
            | WHERE (DATE_EXTRACT("hour_of_day", @timestamp) >= 9 AND DATE_EXTRACT("hour_of_day", @timestamp) <= 17)
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
            | WHERE DATE_EXTRACT("minute_of_hour", @timestamp) == 0
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
            | WHERE DATE_EXTRACT("second_of_minute", @timestamp) < 30
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
            | KEEP year
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
            | EVAL year = DATE_EXTRACT("year", @timestamp), month = DATE_EXTRACT("month_of_year", @timestamp), day = DATE_EXTRACT("day_of_month", @timestamp)
            | KEEP year, month, day
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
            | EVAL hour = DATE_EXTRACT("hour_of_day", @timestamp)
            | KEEP hour
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_DayOfWeekSundayEqual_InWhere_GeneratesIsoDayNumber()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.DayOfWeek == DayOfWeek.Sunday)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("day_of_week", @timestamp) == 7
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_DayOfWeekSundayNotEqual_InWhere_GeneratesIsoDayNumber()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.DayOfWeek != DayOfWeek.Sunday)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("day_of_week", @timestamp) != 7
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_DayOfWeekCapturedSunday_InWhere_GeneratesIsoDayNumber()
	{
		var day = DayOfWeek.Sunday;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.DayOfWeek == day)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("day_of_week", @timestamp) == 7
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_DayOfWeekReversedOperands_InWhere_GeneratesIsoDayNumber()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => DayOfWeek.Sunday == l.Timestamp.DayOfWeek)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE DATE_EXTRACT("day_of_week", @timestamp) == 7
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_DayOfWeekRelational_InWhere_ThrowsNotSupported()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.DayOfWeek < DayOfWeek.Wednesday);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*day_of_week*");
	}

	[Test]
	public void DateTime_DayOfWeekComparison_InSelect_GeneratesIsoDayNumber()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { IsSunday = l.Timestamp.DayOfWeek == DayOfWeek.Sunday })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL isSunday = (DATE_EXTRACT("day_of_week", @timestamp) == 7)
            | KEEP isSunday
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_DayOfWeekComparedToNonConstantExpression_InWhere_ThrowsNotSupported()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp.DayOfWeek == (DayOfWeek)l.StatusCode);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*non-constant*");
	}
}
