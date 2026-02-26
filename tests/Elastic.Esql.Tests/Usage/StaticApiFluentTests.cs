// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Usage;

/// <summary>
/// Tests for CreateQuery&lt;T&gt;().From(...) API with fluent LINQ method syntax.
/// </summary>
public class FluentApiTests : EsqlTestBase
{
	[Test]
	public void SimpleFilter()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            """.NativeLineEndings());
	}

	[Test]
	public void FilterWithTake()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.Take(10)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            | LIMIT 10
            """.NativeLineEndings());
	}

	[Test]
	public void FilterSortTake()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.OrderByDescending(l => l.Timestamp)
			.Take(100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | SORT @timestamp DESC
            | LIMIT 100
            """.NativeLineEndings());
	}

	[Test]
	public void ExplicitIndexPattern()
	{
		var esql = CreateQuery<LogEntry>()
			.From("my-custom-index-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.Take(10)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM my-custom-index-*
            | WHERE log.level.keyword == "ERROR"
            | LIMIT 10
            """.NativeLineEndings());
	}

	[Test]
	public void Projection()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.Select(l => new { l.Message, l.Timestamp })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            | RENAME @timestamp AS timestamp
            | KEEP message, timestamp
            """.NativeLineEndings());
	}
}
