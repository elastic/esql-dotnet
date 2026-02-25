// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.GroupBy;

public class StronglyTypedGroupByTests : EsqlTestBase
{
	[Test]
	public void GroupBy_MemberInit_WithCount_HonorsJsonPropertyName()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new StatsProjection { Level = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS total_count = COUNT(*) BY log_level = log.level.keyword
			""".NativeLineEndings());
	}

	[Test]
	public void GroupBy_MemberInit_WithSum_HonorsJsonPropertyName()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new StatsProjection { Level = g.Key, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS total_duration = SUM(duration) BY log_level = log.level.keyword
			""".NativeLineEndings());
	}

	[Test]
	public void GroupBy_MemberInit_MultipleAggregations_HonorsJsonPropertyName()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new StatsProjection
			{
				Level = g.Key,
				Count = g.Count(),
				TotalDuration = g.Sum(l => l.Duration),
				AvgDuration = g.Average(l => l.Duration)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS total_count = COUNT(*), total_duration = SUM(duration), avg_duration = AVG(duration) BY log_level = log.level.keyword
			""".NativeLineEndings());
	}
}
