// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.LinqMethods;

public class TakeTests : EsqlTestBase
{
	[Test]
	public void Take_GeneratesLimit()
	{
		var esql = Client.Query<LogEntry>()
			.Take(10)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | LIMIT 10
            """);
	}

	[Test]
	public void Take_One_GeneratesLimitOne()
	{
		var esql = Client.Query<LogEntry>()
			.Take(1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | LIMIT 1
            """);
	}

	[Test]
	public void Where_OrderBy_Take_GeneratesCorrectOrder()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.OrderByDescending(l => l.Timestamp)
			.Take(10)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            | SORT @timestamp DESC
            | LIMIT 10
            """);
	}
}
