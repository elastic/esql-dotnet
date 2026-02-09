// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class MedianTests : EsqlTestBase
{
	[Test]
	public void Median_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				MedianDuration = EsqlFunctions.Median(g, l => l.Duration)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS medianDuration = MEDIAN(duration) BY level = log.level.keyword
            """);
	}

	[Test]
	public void MedianAbsoluteDeviation_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				Mad = EsqlFunctions.MedianAbsoluteDeviation(g, l => l.Duration)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS mad = MEDIAN_ABSOLUTE_DEVIATION(duration) BY level = log.level.keyword
            """);
	}
}
