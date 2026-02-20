// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.GroupBy;

public class SimpleGroupByTests : EsqlTestBase
{
	[Test]
	public void GroupBy_SingleField_WithCount_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new { Level = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY level = log.level.keyword
            """.NativeLineEndings());
	}

	[Test]
	public void GroupBy_SingleField_WithSum_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new { Level = g.Key, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS totalDuration = SUM(duration) BY level = log.level.keyword
            """.NativeLineEndings());
	}

	[Test]
	public void GroupBy_SingleField_WithAverage_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new { Level = g.Key, AvgDuration = g.Average(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS avgDuration = AVG(duration) BY level = log.level.keyword
            """.NativeLineEndings());
	}

	[Test]
	public void GroupBy_SingleField_WithMin_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new { Level = g.Key, MinDuration = g.Min(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS minDuration = MIN(duration) BY level = log.level.keyword
            """.NativeLineEndings());
	}

	[Test]
	public void GroupBy_SingleField_WithMax_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new { Level = g.Key, MaxDuration = g.Max(l => l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS maxDuration = MAX(duration) BY level = log.level.keyword
            """.NativeLineEndings());
	}

	[Test]
	public void GroupBy_SingleField_WithMultipleAggregations_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new
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
            | STATS count = COUNT(*), totalDuration = SUM(duration), avgDuration = AVG(duration) BY level = log.level.keyword
            """.NativeLineEndings());
	}

	[Test]
	public void GroupBy_WithWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 400)
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new { Level = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 400
            | STATS count = COUNT(*) BY level = log.level.keyword
            """.NativeLineEndings());
	}
}
