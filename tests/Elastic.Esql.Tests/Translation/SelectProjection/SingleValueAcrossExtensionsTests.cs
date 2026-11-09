// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Generation;

namespace Elastic.Esql.Tests.Translation.SelectProjection;

/// <summary>
/// A single value keeps its column through the extension methods that leave the rows as they are: Keep, Drop, RawEsql
/// that keeps the element type, and the options-carrying methods. Their own selectors are translated as they were.
/// </summary>
public class SingleValueAcrossExtensionsTests : EsqlTestBase
{
	private static IQueryable<LogEntry> Logs() => CreateQuery<LogEntry>().From("logs-*");

	// Translates a terminal operator over the rows, without running it.
	private static string TranslateTerminal<T>(string method, IQueryable<T> source, params Expression[] arguments) =>
		new EsqlFormatter().Format(QueryProvider.TranslateExpression(
			Expression.Call(typeof(Queryable), method, [typeof(T)], [source.Expression, .. arguments]),
			inlineParameters: true
		));

	[Test]
	public void RawEsql_BetweenComputedAndWhere_KeepsTheResult()
	{
		var esql = Logs()
			.Select(l => l.Duration * 3)
			.RawEsql("LIMIT 10")
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | LIMIT 10
            | WHERE result > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_BetweenFieldAndOrderBy_KeepsTheField()
	{
		var esql = Logs()
			.Select(l => l.Message)
			.RawEsql("WHERE message IS NOT NULL")
			.OrderBy(m => m)
			.Take(3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message IS NOT NULL
            | SORT message
            | LIMIT 3
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_BetweenComputedAndSelect_ReadsTheResult()
	{
		var esql = Logs()
			.Select(l => l.Duration * 3)
			.RawEsql("LIMIT 10")
			.Select(x => x * 2)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | LIMIT 10
            | EVAL result = (result * 2.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_BetweenComputedAndMax_AggregatesTheResult()
	{
		var esql = TranslateTerminal(nameof(Queryable.Max), Logs().Select(l => l.Duration * 3).RawEsql("LIMIT 10"));

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | LIMIT 10
            | STATS max = MAX(result)
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_BetweenFieldAndCount_FiltersOnTheField()
	{
		Expression<Func<string, bool>> predicate = m => m == "ab";

		var esql = TranslateTerminal(nameof(Queryable.Count), Logs().Select(l => l.Message).RawEsql("LIMIT 10"), Expression.Quote(predicate));

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | LIMIT 10
            | WHERE message == "ab"
            | STATS count = COUNT(*)
            """.NativeLineEndings());
	}

	[Test]
	public void KeepByName_BetweenComputedAndWhere_KeepsTheResult()
	{
		var esql = Logs()
			.Select(l => l.Duration * 3)
			.Keep("result")
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | KEEP result
            | WHERE result > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void DropByName_BetweenFieldAndWhere_KeepsTheField()
	{
		var esql = Logs()
			.Select(l => l.Message)
			.RawEsql("EVAL extra = 1")
			.Drop("extra")
			.Where(m => m == "ab")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | EVAL extra = 1
            | DROP extra
            | WHERE message == "ab"
            """.NativeLineEndings());
	}

	[Test]
	public void KeepBySelector_BetweenFieldAndWhere_KeepsTheField()
	{
		var esql = Logs()
			.Select(l => l.Message)
			.Keep(m => m)
			.Where(m => m == "ab")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "ab"
            """.NativeLineEndings());
	}

	[Test]
	public void KeepBySelector_BetweenComputedAndWhere_KeepsTheResult()
	{
		var esql = Logs()
			.Select(l => l.Duration * 3)
			.Keep(x => x)
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void OptionsMethod_BetweenComputedAndWhere_KeepsTheResult()
	{
		var esql = Logs()
			.Select(l => l.Duration * 3)
			.WithQueryableOptions(new TestQueryOptions(TimeZone: "UTC"))
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void RowKeepingMethods_InAChain_KeepTheField()
	{
		var esql = Logs()
			.Select(l => l.Message)
			.RawEsql("EVAL extra = 1")
			.Keep("message", "extra")
			.Drop("extra")
			.Where(m => m == "ab")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | EVAL extra = 1
            | KEEP message, extra
            | DROP extra
            | WHERE message == "ab"
            """.NativeLineEndings());
	}

	[Test]
	public void RowKeepingMethod_AfterAnOperator_KeepsTheField()
	{
		var esql = Logs()
			.Select(l => l.Message)
			.Where(m => m != null)
			.RawEsql("LIMIT 5")
			.OrderBy(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message IS NOT NULL
            | LIMIT 5
            | SORT message
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_BetweenGroupedSingleAggregationAndWhere_IsNotSupported()
	{
		var act = () => Logs()
			.GroupBy(l => l.Level)
			.Select(g => g.Count())
			.RawEsql("LIMIT 5")
			.Where(c => c > 1)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("A single aggregation after GroupBy cannot be followed by Where*");
	}

	[Test]
	public void RawEsql_BetweenGroupedSingleAggregationAndTake_IsTranslated()
	{
		var esql = Logs()
			.GroupBy(l => l.Level)
			.Select(g => g.Count())
			.RawEsql("LIMIT 5")
			.Take(3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY log.level
            | LIMIT 5
            | LIMIT 3
            """.NativeLineEndings());
	}

	// The selectors of Keep and Drop themselves are translated as they were.

	[Test]
	public void KeepBySelector_AfterField_IsLeftAsBefore()
	{
		var esql = Logs()
			.Select(l => l.Message)
			.Keep(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void DropBySelectors_AfterField_KeepTheirMessage()
	{
		var act = () => Logs()
			.Select(l => l.Message)
			.Drop(m => m, m => m)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Cannot extract field name from expression: m");
	}
}

internal static class QueryableOptionsExtensions
{
	/// <summary>An options-carrying method over any queryable, as a downstream executor may declare one.</summary>
	[EsqlQueryOptionsMethod]
	public static IQueryable<T> WithQueryableOptions<T>(this IQueryable<T> source, TestQueryOptions options)
	{
		var method = new Func<IQueryable<T>, TestQueryOptions, IQueryable<T>>(WithQueryableOptions).Method;
		return source.Provider.CreateQuery<T>(Expression.Call(null, method, source.Expression, Expression.Constant(options)));
	}
}
