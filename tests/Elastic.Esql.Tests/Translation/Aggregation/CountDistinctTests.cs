// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class CountDistinctTests : EsqlTestBase
{
	[Test]
	public void CountDistinct_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				UniqueIps = EsqlFunctions.CountDistinct(g, l => l.ClientIp)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS uniqueIps = COUNT_DISTINCT(clientIp.keyword) BY level = log.level.keyword
            """);
	}
}
