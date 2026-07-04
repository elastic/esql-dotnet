// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class WeightedAvgTests : EsqlTestBase
{
	[Test]
	public void WeightedAvg_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, Wavg = EsqlFunctions.WeightedAvg(g, l => l.Duration, l => l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS wavg = WEIGHTED_AVG(duration, statusCode) BY level = log.level
			""".NativeLineEndings());
	}
}
