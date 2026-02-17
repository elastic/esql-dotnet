// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class DateTimeArithmeticTests : EsqlTestBase
{
	[Test]
	public void DateTime_AddHours_Negative_GeneratesSubtraction()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-1))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > (NOW() - 1 hours)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_AddDays_Negative_GeneratesSubtraction()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp > DateTime.UtcNow.AddDays(-7))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > (NOW() - 7 days)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_AddMinutes_Negative_GeneratesSubtraction()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp > DateTime.UtcNow.AddMinutes(-30))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > (NOW() - 30 minutes)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_AddSeconds_Negative_GeneratesSubtraction()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp > DateTime.UtcNow.AddSeconds(-60))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > (NOW() - 60 seconds)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_AddHours_Positive_GeneratesAddition()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp < DateTime.UtcNow.AddHours(24))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp < (NOW() + 24 hours)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_AddDays_Positive_GeneratesAddition()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp < DateTime.UtcNow.AddDays(30))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp < (NOW() + 30 days)
            """.NativeLineEndings());
	}

	[Test]
	public void DateTime_BetweenRange_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp >= DateTime.UtcNow.AddDays(-7) && l.Timestamp <= DateTime.UtcNow)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (@timestamp >= (NOW() - 7 days) AND @timestamp <= NOW())
            """.NativeLineEndings());
	}
}
