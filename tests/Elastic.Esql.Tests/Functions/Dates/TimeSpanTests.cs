// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class TimeSpanTests : EsqlTestBase
{
	[Test]
	public void TimeSpan_FromHours_InWhere_GeneratesTimeInterval()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > EsqlFunctions.Now() - TimeSpan.FromHours(1))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > NOW() - 1 hours
            """);
	}

	[Test]
	public void TimeSpan_FromDays_InWhere_GeneratesTimeInterval()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > EsqlFunctions.Now() - TimeSpan.FromDays(7))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > NOW() - 7 days
            """);
	}

	[Test]
	public void TimeSpan_FromMinutes_InWhere_GeneratesTimeInterval()
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
	public void TimeSpan_FromSeconds_InWhere_GeneratesTimeInterval()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > EsqlFunctions.Now() - TimeSpan.FromSeconds(60))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > NOW() - 60 seconds
            """);
	}

	[Test]
	public void TimeSpan_FromMilliseconds_InWhere_GeneratesTimeInterval()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > EsqlFunctions.Now() - TimeSpan.FromMilliseconds(500))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > NOW() - 500 milliseconds
            """);
	}

	[Test]
	public void TimeSpan_LargeValue_InWhere_GeneratesTimeInterval()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > EsqlFunctions.Now() - TimeSpan.FromDays(30))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > NOW() - 30 days
            """);
	}

	[Test]
	public void TimeSpan_Addition_InWhere_GeneratesTimeInterval()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp < EsqlFunctions.Now() + TimeSpan.FromHours(24))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp < NOW() + 24 hours
            """);
	}
}
