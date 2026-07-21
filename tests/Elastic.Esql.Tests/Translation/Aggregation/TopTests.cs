// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class TopTests : EsqlTestBase
{
	[Test]
	public void Top_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, TopDurations = EsqlFunctions.Top(g, l => l.Duration, 3, "desc") })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS topDurations = TOP(duration, 3, "desc") BY level = log.level
			""".NativeLineEndings());
	}
}
