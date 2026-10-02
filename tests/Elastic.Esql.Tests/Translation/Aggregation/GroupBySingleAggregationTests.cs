// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Generation;

namespace Elastic.Esql.Tests.Translation.Aggregation;

/// <summary>
/// A single aggregation after GroupBy is translated into STATS beside the key. The operators that do not read its
/// value go on as before; the translated operators that would have to name its column are refused; any other operator
/// is left to the translation, which refuses it as it did before.
/// </summary>
public class GroupBySingleAggregationTests : EsqlTestBase
{
	private static readonly int[] Other = [1];

	private static IQueryable<int> CountByLevel() =>
		CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => g.Count());

	// Translates an operator over the counts, a terminal one included, without running it.
	private static string Translate(string method, Type[] typeArguments, params Expression[] arguments) =>
		new EsqlFormatter().Format(QueryProvider.TranslateExpression(
			Expression.Call(typeof(Queryable), method, typeArguments, [CountByLevel().Expression, .. arguments]),
			inlineParameters: true));

	private static string Translate(string method, LambdaExpression? argument = null) =>
		argument is null
			? Translate(method, [typeof(int)])
			: Translate(method, [typeof(int)], Expression.Quote(argument));

	[Test]
	public void Take_AfterSingleAggregation_LimitsTheGroups()
	{
		var esql = CountByLevel().Take(5).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY log.level
            | LIMIT 5
            """.NativeLineEndings());
	}

	[Test]
	[Arguments(nameof(Queryable.First), "| LIMIT 1")]
	[Arguments(nameof(Queryable.FirstOrDefault), "| LIMIT 1")]
	[Arguments(nameof(Queryable.Single), "| LIMIT 2")]
	[Arguments(nameof(Queryable.SingleOrDefault), "| LIMIT 2")]
	[Arguments(nameof(Queryable.Count), "| STATS count = COUNT(*)")]
	[Arguments(nameof(Queryable.LongCount), "| STATS count = COUNT(*)")]
	[Arguments(nameof(Queryable.Any), "| STATS result = COUNT(*)\n| EVAL result = result > 0")]
	public void TerminalWithoutPredicate_AfterSingleAggregation_IsTranslated(string method, string tail)
	{
		var esql = Translate(method);

		_ = esql.Should().Be(("FROM logs-*\n| STATS count = COUNT(*) BY log.level\n" + tail).NativeLineEndings());
	}

	[Test]
	public void First_AfterTakeAfterSingleAggregation_IsTranslated()
	{
		var esql = new EsqlFormatter().Format(QueryProvider.TranslateExpression(
			Expression.Call(typeof(Queryable), nameof(Queryable.First), [typeof(int)], CountByLevel().Take(5).Expression),
			inlineParameters: true));

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY log.level
            | LIMIT 5
            | LIMIT 1
            """.NativeLineEndings());
	}

	[Test]
	public void Take_AfterOtherSingleAggregation_LimitsTheGroups()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => g.Sum(x => x.Duration))
			.Take(3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS sum = SUM(duration) BY log.level
            | LIMIT 3
            """.NativeLineEndings());
	}

	[Test]
	public void Take_AfterSingleAggregationOverOneGroup_LimitsTheCount()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => 1)
			.Select(g => g.Count())
			.Take(1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*)
            | LIMIT 1
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AfterSingleAggregation_IsNotSupported()
	{
		var act = () => CountByLevel().Where(c => c > 5).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*followed by Where*g => new { Count = g.Count() }*");
	}

	[Test]
	public void OrderBy_AfterSingleAggregation_IsNotSupported()
	{
		var act = () => CountByLevel().OrderBy(c => c).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*followed by OrderBy*");
	}

	[Test]
	public void OrderByDescending_AfterSingleAggregation_IsNotSupported()
	{
		var act = () => CountByLevel().OrderByDescending(c => c).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*followed by OrderByDescending*");
	}

	[Test]
	public void Select_AfterSingleAggregation_IsNotSupported()
	{
		var act = () => CountByLevel().Select(c => c * 2).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*followed by Select*");
	}

	[Test]
	public void GroupBy_AfterSingleAggregation_IsNotSupported()
	{
		var act = () => CountByLevel().GroupBy(c => c).Select(g => new { g.Key, Groups = g.Count() }).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*followed by GroupBy*");
	}

	[Test]
	public void Where_AfterTakeAfterSingleAggregation_IsNotSupported()
	{
		var act = () => CountByLevel().Take(5).Where(c => c > 5).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*followed by Where*");
	}

	[Test]
	public void Where_AfterConvertedSingleAggregation_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => (long)g.Count())
			.Where(c => c > 5)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*followed by Where*");
	}

	[Test]
	public void Where_AfterSingleAggregationOverOneGroup_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => 1)
			.Select(g => g.Count())
			.Where(c => c > 5)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*followed by Where*");
	}

	[Test]
	[Arguments(nameof(Queryable.First))]
	[Arguments(nameof(Queryable.FirstOrDefault))]
	[Arguments(nameof(Queryable.Single))]
	[Arguments(nameof(Queryable.SingleOrDefault))]
	[Arguments(nameof(Queryable.Count))]
	[Arguments(nameof(Queryable.LongCount))]
	[Arguments(nameof(Queryable.Any))]
	public void TerminalWithPredicate_AfterSingleAggregation_IsNotSupported(string method)
	{
		Expression<Func<int, bool>> predicate = c => c > 5;

		var act = () => Translate(method, predicate);

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"*followed by {method}*");
	}

	[Test]
	[Arguments(nameof(Queryable.Sum))]
	[Arguments(nameof(Queryable.Average))]
	[Arguments(nameof(Queryable.Min))]
	[Arguments(nameof(Queryable.Max))]
	public void AggregateWithoutSelector_AfterSingleAggregation_IsNotSupported(string method)
	{
		// Sum and Average have one overload per type, Min and Max a generic one
		var act = () => method is nameof(Queryable.Sum) or nameof(Queryable.Average)
			? new EsqlFormatter().Format(QueryProvider.TranslateExpression(
				Expression.Call(typeof(Queryable), method, null, CountByLevel().Expression), inlineParameters: true))
			: Translate(method);

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"*followed by {method}*");
	}

	[Test]
	[Arguments(nameof(Queryable.Sum))]
	[Arguments(nameof(Queryable.Average))]
	[Arguments(nameof(Queryable.Min))]
	[Arguments(nameof(Queryable.Max))]
	public void AggregateWithSelector_AfterSingleAggregation_IsNotSupported(string method)
	{
		Expression<Func<int, int>> selector = c => c;

		var act = () => method is nameof(Queryable.Min) or nameof(Queryable.Max)
			? Translate(method, [typeof(int), typeof(int)], Expression.Quote(selector))
			: Translate(method, selector);

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"*followed by {method}*");
	}

	[Test]
	public void KeySelectorThenOrderBy_KeepsTheGroupByMessage()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => g.Key)
			.OrderBy(k => k)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*'g.Key' is neither*");
	}

	[Test]
	public void MemberAfterGroupBy_IsReadByWhere()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new { Count = g.Count() })
			.Where(x => x.Count > 5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY log.level
            | WHERE count > 5
            """.NativeLineEndings());
	}

	// The operators the translation does not support over a row keep the message it gave before.

	[Test]
	public void Join_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var languages = CreateQuery<LanguageLookup>().From("languages_lookup");

		var act = () => CountByLevel().Join(languages, c => c, x => x.LanguageCode, (c, x) => x.LanguageName).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Cannot extract field name from expression: c");
	}

	[Test]
	public void GroupJoin_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var languages = CreateQuery<LanguageLookup>().From("languages_lookup");

		var act = () => CountByLevel().GroupJoin(languages, c => c, x => x.LanguageCode, (c, xs) => c).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("GroupJoin must be followed by SelectMany*");
	}

	[Test]
	public void SelectMany_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().SelectMany(c => new[] { c }).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("SelectMany is only supported as part of a left outer join*");
	}

	[Test]
	public void Skip_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().Skip(1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("'Skip' is not directly supported*");
	}

	[Test]
	public void Distinct_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().Distinct().ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("'Distinct' is not directly supported*");
	}

	[Test]
	public void TakeWhile_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().TakeWhile(c => c > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.TakeWhile' is not supported*");
	}

	[Test]
	public void SkipWhile_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().SkipWhile(c => c > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.SkipWhile' is not supported*");
	}

	[Test]
	public void Zip_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().Zip(Other, (a, b) => a + b).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.Zip' is not supported*");
	}

	[Test]
	public void DistinctBy_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().DistinctBy(c => c).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.DistinctBy' is not supported*");
	}

	[Test]
	public void Cast_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().Cast<int>().ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.Cast' is not supported*");
	}

	[Test]
	public void OfType_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().OfType<int>().ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.OfType' is not supported*");
	}

	[Test]
	public void Reverse_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().Reverse().ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.Reverse' is not supported*");
	}

	[Test]
	public void DefaultIfEmpty_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().DefaultIfEmpty().ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.DefaultIfEmpty' is not supported*");
	}

	[Test]
	public void Concat_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().Concat(Other).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.Concat' is not supported*");
	}

	[Test]
	public void Append_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => CountByLevel().Append(1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.Append' is not supported*");
	}

	[Test]
	[Arguments(nameof(Queryable.Last))]
	[Arguments(nameof(Queryable.LastOrDefault))]
	public void UnsupportedTerminal_AfterSingleAggregation_KeepsTheTranslationMessage(string method)
	{
		var act = () => Translate(method);

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"Method 'Queryable.{method}' is not supported*");
	}

	[Test]
	[Arguments(nameof(Queryable.Last))]
	[Arguments(nameof(Queryable.LastOrDefault))]
	[Arguments(nameof(Queryable.All))]
	public void UnsupportedTerminalWithPredicate_AfterSingleAggregation_KeepsTheTranslationMessage(string method)
	{
		Expression<Func<int, bool>> predicate = c => c > 5;

		var act = () => Translate(method, predicate);

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"Method 'Queryable.{method}' is not supported*");
	}

	[Test]
	[Arguments(nameof(Queryable.MinBy))]
	[Arguments(nameof(Queryable.MaxBy))]
	public void ByAggregate_AfterSingleAggregation_KeepsTheTranslationMessage(string method)
	{
		Expression<Func<int, int>> key = c => c;

		var act = () => Translate(method, [typeof(int), typeof(int)], Expression.Quote(key));

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"Method 'Queryable.{method}' is not supported*");
	}

	[Test]
	public void Aggregate_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		Expression<Func<int, int, int>> accumulate = (a, b) => a + b;

		var act = () => Translate(nameof(Queryable.Aggregate), accumulate);

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.Aggregate' is not supported*");
	}

	[Test]
	public void ElementAt_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => Translate(nameof(Queryable.ElementAt), [typeof(int)], Expression.Constant(1));

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.ElementAt' is not supported*");
	}

	[Test]
	public void Contains_AfterSingleAggregation_KeepsTheTranslationMessage()
	{
		var act = () => Translate(nameof(Queryable.Contains), [typeof(int)], Expression.Constant(1));

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.Contains' is not supported*");
	}
}
