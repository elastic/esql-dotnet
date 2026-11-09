// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Generation;

namespace Elastic.Esql.Tests.Translation.SelectProjection;

/// <summary>
/// Rows whose single value has no known column, such as those of a RawEsql to a single-value type: an operator that
/// reads the value is refused, since its operand would name no column. One that does not read it is translated as
/// before, and an operator the translation does not support keeps its own message.
/// </summary>
public class SingleValueWithoutColumnTests : EsqlTestBase
{
	private static readonly double[] Thresholds = [0.5, 2];

	private static IQueryable<LogEntry> Logs() => CreateQuery<LogEntry>().From("logs-*");

	private static IQueryable<double> Durations() => Logs().RawEsql<LogEntry, double>("EVAL x = duration | KEEP x");

	// Translates an operator over the durations, a terminal one included, without running it.
	private static string Translate(string method, Type[]? typeArguments, params Expression[] arguments) =>
		new EsqlFormatter().Format(QueryProvider.TranslateExpression(
			Expression.Call(typeof(Queryable), method, typeArguments, [Durations().Expression, .. arguments]),
			inlineParameters: true
		));

	private static string Translate(string method, LambdaExpression? argument = null) =>
		argument is null
			? Translate(method, [typeof(double)])
			: Translate(method, [typeof(double)], Expression.Quote(argument));

	[Test]
	public void Where_ReadingTheValue_IsNotSupported()
	{
		var act = () => Durations().Where(x => x > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("Where cannot read the single value of these rows*project the value into a member*");
	}

	[Test]
	public void OrderBy_ReadingTheValue_IsNotSupported()
	{
		var act = () => Durations().OrderBy(x => x).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("OrderBy cannot read the single value*");
	}

	[Test]
	public void OrderByDescending_ReadingTheValue_IsNotSupported()
	{
		var act = () => Durations().OrderByDescending(x => x).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("OrderByDescending cannot read the single value*");
	}

	[Test]
	public void Select_ReadingTheValue_IsNotSupported()
	{
		var act = () => Durations().Select(x => x * 2).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Select cannot read the single value*");
	}

	[Test]
	public void GroupBy_ReadingTheValue_IsNotSupported()
	{
		var act = () => Durations().GroupBy(x => x).Select(g => new { g.Key, Count = g.Count() }).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("GroupBy cannot read the single value*");
	}

	[Test]
	[Arguments(nameof(Queryable.First))]
	[Arguments(nameof(Queryable.FirstOrDefault))]
	[Arguments(nameof(Queryable.Single))]
	[Arguments(nameof(Queryable.SingleOrDefault))]
	[Arguments(nameof(Queryable.Count))]
	[Arguments(nameof(Queryable.LongCount))]
	[Arguments(nameof(Queryable.Any))]
	public void TerminalWithPredicate_ReadingTheValue_IsNotSupported(string method)
	{
		Expression<Func<double, bool>> predicate = x => x > 1;

		var act = () => Translate(method, predicate);

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"{method} cannot read the single value*");
	}

	[Test]
	[Arguments(nameof(Queryable.Sum))]
	[Arguments(nameof(Queryable.Average))]
	[Arguments(nameof(Queryable.Min))]
	[Arguments(nameof(Queryable.Max))]
	public void AggregateWithoutSelector_IsNotSupported(string method)
	{
		// Sum and Average have one overload per type, Min and Max a generic one
		var act = () => method is nameof(Queryable.Sum) or nameof(Queryable.Average)
			? Translate(method, typeArguments: null)
			: Translate(method);

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"{method} cannot read the single value*");
	}

	[Test]
	[Arguments(nameof(Queryable.Sum))]
	[Arguments(nameof(Queryable.Average))]
	[Arguments(nameof(Queryable.Min))]
	[Arguments(nameof(Queryable.Max))]
	public void AggregateWithSelector_ReadingTheValue_IsNotSupported(string method)
	{
		Expression<Func<double, double>> selector = x => x;

		var act = () => method is nameof(Queryable.Min) or nameof(Queryable.Max)
			? Translate(method, [typeof(double), typeof(double)], Expression.Quote(selector))
			: Translate(method, selector);

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"{method} cannot read the single value*");
	}

	[Test]
	[Arguments(nameof(Enumerable.Sum))]
	[Arguments(nameof(Enumerable.Average))]
	[Arguments(nameof(Enumerable.Min))]
	[Arguments(nameof(Enumerable.Max))]
	public void AggregateOfAGroup_IsNotSupported(string method)
	{
		var grouped = Durations().GroupBy(x => 1);

		var act = () => method switch
		{
			nameof(Enumerable.Sum) => grouped.Select(g => new { V = g.Sum() }).ToString(),
			nameof(Enumerable.Average) => grouped.Select(g => new { V = g.Average() }).ToString(),
			nameof(Enumerable.Min) => grouped.Select(g => new { V = g.Min() }).ToString(),
			_ => grouped.Select(g => new { V = g.Max() }).ToString()
		};

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"{method} cannot read the single value of these rows*");
	}

	[Test]
	public void AggregateOfAFilteredGroup_IsNotSupported()
	{
		var act = () => Durations().GroupBy(x => 1).Select(g => new { V = g.Where(x => x > 1).Sum() }).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Sum over a group that is filtered or transformed*");
	}

	[Test]
	public void SingleAggregationOfAGroup_IsNotSupported()
	{
		var act = () => Durations().GroupBy(x => 1).Select(g => g.Sum()).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Sum cannot read the single value of these rows*");
	}

	[Test]
	public void CountOfAGroup_IsTranslated()
	{
		var esql = Durations().GroupBy(x => 1).Select(g => new { N = g.Count() }).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL x = duration | KEEP x
            | STATS n = COUNT(*)
            """.NativeLineEndings());
	}

	// An operator that does not read the value is translated as before.

	[Test]
	public void Take_IsTranslated()
	{
		var esql = Durations().Take(5).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL x = duration | KEEP x
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
	public void TerminalWithoutPredicate_IsTranslated(string method, string tail)
	{
		var esql = Translate(method);

		_ = esql.Should().Be(("FROM logs-*\n| EVAL x = duration | KEEP x\n" + tail).NativeLineEndings());
	}

	[Test]
	public void Where_NotReadingTheValue_IsTranslated()
	{
		var esql = Durations().Where(x => true).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL x = duration | KEEP x
            | WHERE true
            """.NativeLineEndings());
	}

	[Test]
	public void Where_WithALambdaOfItsOwnNotReadingTheValue_KeepsTheTranslationMessage()
	{
		var act = () => Durations().Where(x => Thresholds.Any(t => t > 1)).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method Enumerable.Any is not supported.");
	}

	[Test]
	[Arguments(nameof(Queryable.First), "| LIMIT 1")]
	[Arguments(nameof(Queryable.FirstOrDefault), "| LIMIT 1")]
	[Arguments(nameof(Queryable.Single), "| LIMIT 2")]
	[Arguments(nameof(Queryable.SingleOrDefault), "| LIMIT 2")]
	[Arguments(nameof(Queryable.Count), "| STATS count = COUNT(*)")]
	[Arguments(nameof(Queryable.LongCount), "| STATS count = COUNT(*)")]
	[Arguments(nameof(Queryable.Any), "| STATS result = COUNT(*)\n| EVAL result = result > 0")]
	public void TerminalWithPredicate_NotReadingTheValue_IsTranslated(string method, string tail)
	{
		Expression<Func<double, bool>> predicate = x => true;

		var esql = Translate(method, predicate);

		_ = esql.Should().Be(("FROM logs-*\n| EVAL x = duration | KEEP x\n| WHERE true\n" + tail).NativeLineEndings());
	}

	[Test]
	public void Select_NotReadingTheValue_IsTranslated()
	{
		var esql = Durations().Select(x => 1).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL x = duration | KEEP x
            | EVAL result = 1
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_Identity_IsTranslated()
	{
		var esql = Durations().Select(x => x).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL x = duration | KEEP x
            """.NativeLineEndings());
	}

	[Test]
	public void Select_IdentityThroughAConversion_IsTranslated()
	{
		var esql = Durations().Select(x => (object)x).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL x = duration | KEEP x
            """.NativeLineEndings());
	}

	// An operator the translation does not support keeps its own message, even before one that reads the value.

	[Test]
	public void Skip_BeforeAnOperatorReadingTheValue_KeepsItsMessage()
	{
		var act = () => Durations().Skip(1).Where(x => x > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("'Skip' is not directly supported*");
	}

	[Test]
	public void Distinct_BeforeAnOperatorReadingTheValue_KeepsItsMessage()
	{
		var act = () => Durations().Distinct().Where(x => x > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("'Distinct' is not directly supported*");
	}

	[Test]
	public void TakeWhile_ReadingTheValue_KeepsTheTranslationMessage()
	{
		var act = () => Durations().TakeWhile(x => x > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.TakeWhile' is not supported*");
	}

	[Test]
	public void MethodOfAnotherClassNamedLikeAnOperator_KeepsTheTranslationMessage()
	{
		var act = () => Durations().Where(x => x > 1, other: true).ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("Method 'OtherQueryableExtensions.Where' is not supported in ES|QL translation.");
	}

	// The rows that hold a single value without a known column.

	[Test]
	public void Completion_BetweenFieldAndWhere_IsNotSupported()
	{
		var act = () => Logs()
			.Select(l => l.Message)
			.Completion("Summarize", "my-endpoint")
			.Where(m => m == "ab")
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where cannot read the single value*");
	}

	[Test]
	public void RawEsqlToANewType_BetweenComputedAndWhere_IsNotSupported()
	{
		var act = () => Logs()
			.Select(l => l.Duration * 3)
			.RawEsql<double, double>("EVAL y = result | KEEP y")
			.Where(x => x > 1)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where cannot read the single value*");
	}

	[Test]
	public void ForkBranch_ReadingTheValue_IsNotSupported()
	{
		var act = () => Logs()
			.Select(l => l.Duration * 3)
			.Fork(b => b.Where(x => x > 1), b => b.Take(1))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where cannot read the single value*");
	}

	[Test]
	public void ForkBranches_NotReadingTheValue_AreTranslated()
	{
		var esql = Logs()
			.Select(l => l.Duration * 3)
			.Fork(b => b.Take(1), b => b.Take(2))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | FORK (LIMIT 1) (LIMIT 2)
            """.NativeLineEndings());
	}

	[Test]
	public void Fuse_BetweenForkAndWhere_IsNotSupported()
	{
		var act = () => Logs()
			.Select(l => l.Duration * 3)
			.Fork(b => b.Take(1), b => b.Take(2))
			.Fuse()
			.Where(x => x > 1)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where cannot read the single value*");
	}

	[Test]
	public void LookupJoinToASingleValue_ThenWhere_IsNotSupported()
	{
		var act = () => Logs()
			.LookupJoin<LogEntry, ThreatListEntry, string?, string>("threat_list", l => l.ClientIp, t => t.ClientIp, (l, t) => t!.ThreatLevel)
			.Where(x => x == "high")
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where cannot read the single value*");
	}

	[Test]
	public void QueryOfSingleValues_ThenWhere_IsNotSupported()
	{
		var act = () => CreateQuery<string>().From("names").Where(s => s == "ab").ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where cannot read the single value*");
	}

	[Test]
	public void QueryOfSingleValues_ThenIdentity_IsTranslated()
	{
		var esql = CreateQuery<string>().From("names").Select(s => s).ToString();

		_ = esql.Should().Be("FROM names");
	}

	[Test]
	public void QueryOfSingleValues_ThenTake_IsTranslated()
	{
		var esql = CreateQuery<string>().From("names").Take(5).ToString();

		_ = esql.Should().Be(
			"""
            FROM names
            | LIMIT 5
            """.NativeLineEndings());
	}
}

internal static class OtherQueryableExtensions
{
	/// <summary>A method of another library that shares the name of a Queryable operator.</summary>
	public static IQueryable<T> Where<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, bool other)
	{
		var method = new Func<IQueryable<T>, Expression<Func<T, bool>>, bool, IQueryable<T>>(Where).Method;
		return source.Provider.CreateQuery<T>(
			Expression.Call(null, method, source.Expression, Expression.Quote(predicate), Expression.Constant(other))
		);
	}
}
