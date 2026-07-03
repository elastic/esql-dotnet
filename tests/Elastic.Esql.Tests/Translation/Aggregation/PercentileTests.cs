// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class PercentileTests : EsqlTestBase
{
	[Test]
	public void Percentile_FractionalValue_FormatsWithInvariantCulture()
	{
		var originalCulture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("de-DE");

		try
		{
			var esql = CreateQuery<LogEntry>()
				.From("logs-*")
				.GroupBy(l => l.Level.MultiField("keyword"))
				.Select(g => new
				{
					Level = g.Key,
					P999 = EsqlFunctions.Percentile(g, l => l.Duration, 99.9)
				})
				.ToString();

			_ = esql.Should().Be(
				"""
				FROM logs-*
				| STATS p999 = PERCENTILE(duration, 99.9) BY level = log.level.keyword
				""".NativeLineEndings());
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

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
