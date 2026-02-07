// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Usage;

/// <summary>
/// Tests for static Esql.From&lt;T&gt;() API with fluent LINQ method syntax.
/// </summary>
public class StaticApiFluentTests : EsqlTestBase
{
	[Test]
	public void SimpleFilter()
	{
		var esql = Esql.InMemory<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            """);
	}

	[Test]
	public void FilterWithTake()
	{
		var esql = Esql.InMemory<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.Take(10)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            | LIMIT 10
            """);
	}

	[Test]
	public void FilterSortTake()
	{
		var esql = Esql.InMemory<LogEntry>()
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
            """);
	}

	[Test]
	public void ExplicitIndexPattern()
	{
		var esql = Esql.InMemory<LogEntry>("my-custom-index-*")
			.Where(l => l.Level == "ERROR")
			.Take(10)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM my-custom-index-*
            | WHERE log.level == "ERROR"
            | LIMIT 10
            """);
	}

	[Test]
	public void Projection()
	{
		var esql = Esql.InMemory<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.Select(l => new { l.Message, l.Timestamp })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            | KEEP message
            | EVAL timestamp = @timestamp
            """);
	}
}
