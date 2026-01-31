// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class DateTimeNowTests : EsqlTestBase
{
	[Test]
	public void DateTime_UtcNow_InWhere_GeneratesNow()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-1))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > (NOW() - 1 hours)
            """);
	}

	[Test]
	public void DateTime_Now_InWhere_GeneratesNow()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > DateTime.Now.AddDays(-7))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > (NOW() - 7 days)
            """);
	}

	[Test]
	public void DateTime_Today_InWhere_GeneratesDateTruncNow()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp >= DateTime.Today)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp >= DATE_TRUNC("day", NOW())
            """);
	}

	[Test]
	public void EsqlFunction_Now_WithSubtraction_GeneratesNow()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > EsqlFunctions.Now() - TimeSpan.FromMinutes(30))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > NOW() - 30 minutes
            """);
	}

	[Test]
	public void DateTime_UtcNow_Comparison_GeneratesNow()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp < DateTime.UtcNow)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp < NOW()
            """);
	}

	[Test]
	public void DateTime_Now_InSelect_GeneratesNow()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { l.Message, CurrentTime = EsqlFunctions.Now() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | EVAL currentTime = NOW()
            """);
	}
}
