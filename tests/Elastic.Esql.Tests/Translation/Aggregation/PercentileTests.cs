// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class PercentileTests : EsqlTestBase
{
	[Test]
	public void Percentile_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new
			{
				Level = g.Key,
				P99 = EsqlFunctions.Percentile(g, l => l.Duration, 99)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS p99 = PERCENTILE(duration, 99) BY level = log.level.keyword
            """.NativeLineEndings());
	}
}
