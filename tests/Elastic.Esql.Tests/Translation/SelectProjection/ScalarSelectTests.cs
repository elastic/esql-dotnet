// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class ScalarSelectTests : EsqlTestBase
{
	[Test]
	public void Select_Arithmetic_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_MathFunction_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => Math.Floor(l.Duration))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = FLOOR(duration)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_MathRound_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => Math.Round(l.Duration))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = ROUND(duration)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_StringMethod_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message.ToUpperInvariant())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_UPPER(message)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_EsqlFunction_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => EsqlFunctions.Abs(l.Duration))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = ABS(duration)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_Comparison_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (statusCode >= 500)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenWhere_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > 5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 5.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenOrderBy_SortsOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.OrderBy(x => x)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | SORT result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedAfterWhereThenTake_GeneratesEvalAndLimit()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.IsError)
			.Select(l => l.Duration * 3)
			.Take(5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE isError
            | EVAL result = (duration * 3.0)
            | KEEP result
            | LIMIT 5
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m == "a")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "a"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenOrderBy_SortsOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.OrderBy(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | SORT message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenObject_EvaluatesIntoTheMember()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Select(x => new { V = x })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL v = (duration * 3.0)
            | KEEP v
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedWithMetadata_KeepsOnlyTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Id)
			.Select(l => l.Duration * 3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-* METADATA _id
            | EVAL result = (duration * 3.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldWithMetadata_KeepsOnlyTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Id)
			.Select(l => l.Message)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-* METADATA _id
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_Constant_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = 1
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_RenamedFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Level)
			.Where(v => v == "x")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP log.level
            | WHERE log.level == "x"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_DateFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Timestamp)
			.Where(t => t > new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP @timestamp
            | WHERE @timestamp > "2024-01-01T00:00:00.000Z"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenGroupBy_GroupsOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.GroupBy(m => m)
			.Select(g => new { g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | STATS count = COUNT(*) BY key = message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenGroupByThenSum_AggregatesTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.GroupBy(x => x)
			.Select(g => new { g.Key, Total = g.Sum() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | STATS total = SUM(result) BY key = result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenGroupByThenMax_AggregatesTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.GroupBy(x => x)
			.Select(g => new { g.Key, Max = g.Max() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP duration
            | STATS max = MAX(duration) BY key = duration
            """.NativeLineEndings());
	}

	[Test]
	[Arguments(nameof(Enumerable.Sum), "SUM")]
	[Arguments(nameof(Enumerable.Average), "AVG")]
	[Arguments(nameof(Enumerable.Min), "MIN")]
	[Arguments(nameof(Enumerable.Max), "MAX")]
	public void Select_FieldThenWhereThenGroupByThenAggregate_AggregatesTheField(string method, string function)
	{
		var grouped = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.Where(x => x > 1)
			.GroupBy(x => x);

		var esql = method switch
		{
			nameof(Enumerable.Sum) => grouped.Select(g => new { g.Key, V = g.Sum() }).ToString(),
			nameof(Enumerable.Average) => grouped.Select(g => new { g.Key, V = g.Average() }).ToString(),
			nameof(Enumerable.Min) => grouped.Select(g => new { g.Key, V = g.Min() }).ToString(),
			_ => grouped.Select(g => new { g.Key, V = g.Max() }).ToString()
		};

		_ = esql.Should().Be(
			$"""
            FROM logs-*
            | KEEP duration
            | WHERE duration > 1.0
            | STATS v = {function}(duration) BY key = duration
            """.NativeLineEndings());
	}

	[Test]
	[Arguments(nameof(Enumerable.Sum))]
	[Arguments(nameof(Enumerable.Average))]
	[Arguments(nameof(Enumerable.Min))]
	[Arguments(nameof(Enumerable.Max))]
	public void Select_FieldThenGroupByThenAggregateOfAFilteredGroup_IsNotSupported(string method)
	{
		var grouped = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.GroupBy(x => 1);

		var act = () => method switch
		{
			nameof(Enumerable.Sum) => grouped.Select(g => new { V = g.Where(x => x > 10).Sum() }).ToString(),
			nameof(Enumerable.Average) => grouped.Select(g => new { V = g.Where(x => x > 10).Average() }).ToString(),
			nameof(Enumerable.Min) => grouped.Select(g => new { V = g.Where(x => x > 10).Min() }).ToString(),
			_ => grouped.Select(g => new { V = g.Where(x => x > 10).Max() }).ToString()
		};

		_ = act.Should().Throw<NotSupportedException>().WithMessage($"{method} over a group that is filtered or transformed*");
	}

	[Test]
	public void Select_ComputedThenGroupByThenSumOfAProjectedGroup_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.GroupBy(x => x)
			.Select(g => new { g.Key, Total = g.Select(x => x * 2).Sum() })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Sum over a group that is filtered or transformed*");
	}

	[Test]
	public void Select_FieldThenGroupByThenSingleSumOfAFilteredGroup_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.GroupBy(x => x)
			.Select(g => g.Where(x => x > 10).Sum())
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Sum over a group that is filtered or transformed*");
	}

	[Test]
	public void Select_ComputedThenGroupByWithIdentityElementThenSum_AggregatesTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.GroupBy(x => x, x => x)
			.Select(g => new { g.Key, Total = g.Sum() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | STATS total = SUM(result) BY key = result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenGroupByThenCount_CountsTheRows()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.GroupBy(x => x)
			.Select(g => new { g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | STATS count = COUNT(*) BY key = result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenGroupByThenSingleSum_AggregatesTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.GroupBy(x => x)
			.Select(g => g.Sum())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | STATS sum = SUM(result) BY result
            """.NativeLineEndings());
	}

	// A computed string can be null, as the member of an anonymous type can: an ordering comparison says how it treats null.

	[Test]
	public void Select_ComputedStringThenOrdinalLessThan_KeepsTheNullRows()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message.ToUpperInvariant())
			.Where(s => string.CompareOrdinal(s, "m") < 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_UPPER(message)
            | KEEP result
            | WHERE (result IS NULL OR result < "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedStringThenOrdinalGreaterThan_LeavesOutTheNullRows()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message.ToUpperInvariant())
			.Where(s => string.CompareOrdinal(s, "m") > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_UPPER(message)
            | KEEP result
            | WHERE (result IS NOT NULL AND result > "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedStringThenOrdinalCompareAtMost_KeepsTheNullRows()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message.ToUpperInvariant())
			.Where(s => string.Compare(s, "m", StringComparison.Ordinal) <= 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_UPPER(message)
            | KEEP result
            | WHERE (result IS NULL OR result <= "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedStringThenNotEqual_ComparesAsBefore()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message.ToUpperInvariant())
			.Where(s => s != "a")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_UPPER(message)
            | KEEP result
            | WHERE result != "a"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedNumberThenGreaterThan_ComparesAsBefore()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode * 2)
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (statusCode * 2)
            | KEEP result
            | WHERE result > 1
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenOrdinalLessThan_FollowsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(s => string.CompareOrdinal(s, "m") < 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message < "m"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_IntegerFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode)
			.Where(s => s >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode >= 500
            """.NativeLineEndings());
	}

	[Test]
	public void Select_BooleanFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.IsError)
			.Where(e => e)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP isError
            | WHERE isError
            """.NativeLineEndings());
	}

	[Test]
	public void Select_NestedFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<EagerNestedDocument>()
			.From("docs")
			.Select(d => d.Host.Name)
			.Where(n => n == "a")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM docs
            | KEEP host.name
            | WHERE host.name == "a"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_EnumFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<EventDocument>()
			.From("events-*")
			.Select(e => e.Level)
			.Where(l => l == LogLevel.Error)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM events-*
            | KEEP level
            | WHERE level == "Error"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_CastFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (double)l.StatusCode)
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_NullableFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<MetricDocument>()
			.From("metrics")
			.Select(m => m.Count)
			.Where(c => c > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM metrics
            | KEEP count
            | WHERE count > 1
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenStringMethod_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m.StartsWith("ab"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message LIKE "ab*"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenContains_FiltersOnTheField()
	{
		var codes = new[] { 200, 404 };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode)
			.Where(s => codes.Contains(s))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode IN (200, 404)
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenOrderByDescendingThenTake_SortsAndLimits()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode)
			.OrderByDescending(s => s)
			.Take(3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | SORT statusCode DESC
            | LIMIT 3
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldWithMetadataThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Id)
			.Select(l => l.Message)
			.Where(m => m == "a")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-* METADATA _id
            | KEEP message
            | WHERE message == "a"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenOrderByDescending_SortsOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.OrderByDescending(x => x)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | SORT result DESC
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenGroupBy_GroupsOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.GroupBy(x => x)
			.Select(g => new { g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | STATS count = COUNT(*) BY key = result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenWhereThenObject_RenamesTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > 5)
			.Select(x => new { V = x })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 5.0
            | RENAME result AS v
            | KEEP v
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedAfterTake_GeneratesLimitThenEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Take(10)
			.Select(l => l.Duration * 3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | LIMIT 10
            | EVAL result = (duration * 3.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenTwoMembers_EvaluatesBoth()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Select(x => new { A = x, B = x * 2 })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL a = (duration * 3.0), b = ((duration * 3.0) * 2.0)
            | KEEP a, b
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenComputed_EvaluatesOnce()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Select(x => x + 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = ((duration * 3.0) + 1.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenComputed_EvaluatesOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Select(m => m.ToUpperInvariant())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_UPPER(message)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenWhereOnCapturedValue_KeepsTheParameter()
	{
		var threshold = 5.0;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > threshold)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > ?threshold
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedInQuerySyntax_GeneratesEval()
	{
		var esql = (from l in CreateQuery<LogEntry>().From("logs-*")
					where l.IsError
					select l.Duration * 3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE isError
            | EVAL result = (duration * 3.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenOrderByThenByDescending_SortsOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode)
			.OrderBy(s => s)
			.ThenByDescending(s => s)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | SORT statusCode, statusCode DESC
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenWhereWithTwoConditions_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > 1 && x < 10)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE (result > 1.0 AND result < 10.0)
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedOnNestedField_GeneratesEval()
	{
		var esql = CreateQuery<EagerNestedDocument>()
			.From("docs")
			.Select(d => d.Host.Name.ToUpperInvariant())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM docs
            | EVAL result = TO_UPPER(host.name)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedOnRenamedField_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Level.ToUpperInvariant())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_UPPER(log.level)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedBooleanThenWhere_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode >= 500)
			.Where(b => b)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (statusCode >= 500)
            | KEEP result
            | WHERE result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_SingleAggregationAfterGroupBy_IsLeftToStats()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => g.Count())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY log.level
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenIdentity_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Select(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenBoxed_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Select(x => (object)x)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that computes a single value cannot return it as Object*");
	}

	[Test]
	public void Select_ComputedBoxed_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (object)(l.Duration * 3))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that computes a single value cannot return it as Object*");
	}

	[Test]
	public void Select_MemberOfAValueBoxed_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (object)l.Timestamp.Hour)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that computes a single value cannot return it as Object*");
	}

	[Test]
	public void Select_ComputedStringAsAnInterface_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select<LogEntry, IComparable>(l => l.Message.ToUpperInvariant())
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that computes a single value cannot return it as IComparable*");
	}

	[Test]
	public void Select_ConstructedSingleValue_IsNotTakenForAnotherType()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new DateTime(2020, 1, 1))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.Which.Message.Should().NotStartWith("A Select that computes a single value");
	}

	// A conversion to an integral type that cannot hold every value of the source changes the value, as truncation does.

	[Test]
	public void Select_FieldTruncatedThenWhere_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (int)l.Duration)
			.Where(x => x == 2)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Double to Int32 can change the value*");
	}

	[Test]
	public void Select_FieldTruncated_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (int)l.Duration)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Double to Int32 can change the value*");
	}

	[Test]
	public void Select_ComputedTruncated_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (long)(l.Duration * 3))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Double to Int64 can change the value*");
	}

	[Test]
	public void Select_FieldTruncatedThenWidened_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (long)(int)l.Duration)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Double to Int32 can change the value*");
	}

	[Test]
	public void Select_FieldTruncatedInsideAComputation_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (int)l.Duration + 1)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Double to Int32 can change the value*");
	}

	[Test]
	public void Select_FieldThenTruncatedInsideAComputation_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.Select(x => (int)x + 1)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Double to Int32 can change the value*");
	}

	[Test]
	public void Select_FieldThenWhereThenTruncatedInsideAComputation_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.Where(x => x > 0)
			.Select(x => (int)x * 2)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Double to Int32 can change the value*");
	}

	[Test]
	public void Select_FieldNarrowedTwice_NamesTheOuterConversion()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (short)(int)l.Duration)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Int32 to Int16 can change the value*");
	}

	[Test]
	public void Select_FieldNarrowedToAShorterInteger_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (short)l.StatusCode)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Int32 to Int16 can change the value*");
	}

	[Test]
	public void Select_FieldToUnsigned_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (uint)l.StatusCode)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Int32 to UInt32 can change the value*");
	}

	[Test]
	public void Select_FieldTruncatedToNullable_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (int?)l.Duration)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Double to Int32 can change the value*");
	}

	[Test]
	[Arguments("long")]
	[Arguments("double")]
	[Arguments("decimal")]
	[Arguments("int?")]
	public void Select_FieldWidened_KeepsTheField(string target)
	{
		var query = CreateQuery<LogEntry>().From("logs-*");

		var esql = target switch
		{
			"long" => query.Select(l => (long)l.StatusCode).ToString(),
			"double" => query.Select(l => (double)l.StatusCode).ToString(),
			"decimal" => query.Select(l => (decimal)l.StatusCode).ToString(),
			_ => query.Select(l => (int?)l.StatusCode).ToString()
		};

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldWidenedThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (long)l.StatusCode)
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode > 1
            """.NativeLineEndings());
	}

	[Test]
	public void Select_EnumToItsUnderlyingType_KeepsTheField()
	{
		var esql = CreateQuery<OrdinalEnumDocument>()
			.From("docs")
			.Select(d => (int)d.Priority)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM docs
            | KEEP priority
            """.NativeLineEndings());
	}

	[Test]
	public void Select_EnumToAShorterInteger_IsNotSupported()
	{
		var act = () => CreateQuery<OrdinalEnumDocument>()
			.From("docs")
			.Select(d => (byte)d.Priority)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("A Select that converts Priority to Byte can change the value*");
	}

	[Test]
	public void Select_FieldToFloat_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (float)l.Duration)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP duration
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldBoxed_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (object)l.Message)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenWhereThenBoxed_KeepsTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > 1)
			.Select(x => (object)x)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 1.0
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedAsNullable_EvaluatesTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (double?)(l.Duration * 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenIdentityToABaseType_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Select<string, object>(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenIdentityToAnInterface_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Select<string, IComparable>(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenIdentity_KeepsTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Select(x => x)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_StringConstant_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => "x")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = "x"
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhereThenComputed_EvaluatesOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.Where(x => x > 1)
			.Select(x => x * 2)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP duration
            | WHERE duration > 1.0
            | EVAL result = (duration * 2.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenWhereThenComputed_EvaluatesOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > 1)
			.Select(x => x * 2)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 1.0
            | EVAL result = (result * 2.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenWhereThenIdentity_KeepsTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > 1)
			.Select(x => x)
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
	public void Select_NullableComputedThenWhere_FiltersOnTheResult()
	{
		var esql = CreateQuery<MetricDocument>()
			.From("metrics")
			.Select(m => m.Value * 2)
			.Where(v => v > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM metrics
            | EVAL result = (value * 2)
            | KEEP result
            | WHERE result > 1
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedInsideALongChain_KeepsTheOrder()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.IsError)
			.Select(l => l.Duration * 3)
			.Where(x => x > 1)
			.OrderBy(x => x)
			.Take(5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE isError
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 1.0
            | SORT result
            | LIMIT 5
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenTakeThenWhere_LimitsBeforeFiltering()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Take(5)
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | LIMIT 5
            | WHERE result > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedAfterOrderByOnTheDocument_SortsFirst()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Timestamp)
			.Select(l => l.Duration * 3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT @timestamp
            | EVAL result = (duration * 3.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_IntegerDivision_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode / 100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (statusCode / 100)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_CastToNullableFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (int?)l.StatusCode)
			.Where(s => s > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode > 1
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhereThenObject_RenamesTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m == "a")
			.Select(m => new { M = m })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "a"
            | RENAME message AS m
            | KEEP m
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhereReadingTheValueTwice_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m.StartsWith("ab") || m.EndsWith("yz"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE (message LIKE "ab*" OR message LIKE "*yz")
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenNotNull_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.ClientIp!.ToUpperInvariant())
			.Where(s => s != null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_UPPER(clientIp)
            | KEEP result
            | WHERE result IS NOT NULL
            """.NativeLineEndings());
	}

	[Test]
	public void Select_NullTest_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.ClientIp == null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (clientIp IS NULL)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_Conjunction_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.IsError && l.StatusCode > 500)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (isError AND (statusCode > 500))
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_DateFieldThenOrderByDescending_SortsOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Timestamp)
			.OrderByDescending(t => t)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP @timestamp
            | SORT @timestamp DESC
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ThreeSelectsInARow_FoldIntoOneEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.Select(d => d * 2)
			.Select(e => e + 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = ((duration * 2.0) + 1.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenFunctionInWhere_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => EsqlFunctions.Abs(x) > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE ABS(result) > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenConstantOnTheLeft_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => 5 < x)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE 5.0 < result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenIsNullOrEmpty_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => string.IsNullOrEmpty(m))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE (message IS NULL OR message == "")
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenCastInWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode)
			.Where(s => s > 1.5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode > 1.5
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhereThenAnonymousMember_RenamesTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.Where(x => x > 1)
			.Select(x => new { x })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP duration
            | WHERE duration > 1.0
            | RENAME duration AS x
            | KEEP x
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenAsEsqlQueryableThenWhere_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.AsEsqlQueryable()
			.Where(x => x > 5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 5.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedWithoutNamingPolicy_NamesTheColumnAfterTheMember()
	{
		var provider = new EsqlQueryProvider(new JsonSerializerOptions { TypeInfoResolver = EsqlTestMappingContext.Default });

		var esql = new EsqlQueryable<LogEntry>(provider)
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > 5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL Result = (Duration * 3.0)
            | KEEP Result
            | WHERE Result > 5.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedWithSnakeCaseNaming_NamesTheColumnResult()
	{
		var provider = new EsqlQueryProvider(new JsonSerializerOptions
		{
			TypeInfoResolver = EsqlTestMappingContext.Default,
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
		});

		var esql = new EsqlQueryable<LogEntry>(provider)
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedFromAFieldNamedResult_OverwritesIt()
	{
		var esql = CreateQuery<ResultFieldDocument>()
			.From("results")
			.Select(d => d.Result * 2)
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM results
            | EVAL result = (result * 2.0)
            | KEEP result
            | WHERE result > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldNamedResultThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<ResultFieldDocument>()
			.From("results")
			.Select(d => d.Result)
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM results
            | KEEP result
            | WHERE result > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ConstantAfterField_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration)
			.Select(x => 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = 1
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_CastToLongThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (long)l.StatusCode)
			.Where(x => x > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode > 1
            """.NativeLineEndings());
	}

	[Test]
	public void Select_Modulo_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode % 100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (statusCode % 100)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_MathMax_GeneratesGreatest()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => Math.Max(l.Duration, 1))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = GREATEST(duration, 1.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenStringMethodThenOrderBy_ReadsTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message.ToLowerInvariant())
			.Where(m => m.StartsWith("ab"))
			.OrderBy(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = TO_LOWER(message)
            | KEEP result
            | WHERE result LIKE "ab*"
            | SORT result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_EsqlRoundWithDecimals_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => EsqlFunctions.Round(l.Duration, 2))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = ROUND(duration, 2)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_BooleanFieldThenNegation_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.IsError)
			.Where(e => !e)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP isError
            | WHERE NOT isError
            """.NativeLineEndings());
	}

	[Test]
	public void Select_EnumFieldThenTwoConditions_FiltersOnTheField()
	{
		var esql = CreateQuery<EventDocument>()
			.From("events-*")
			.Select(e => e.Level)
			.Where(l => l != LogLevel.Debug && l != LogLevel.Info)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM events-*
            | KEEP level
            | WHERE (level != "Debug" AND level != "Info")
            """.NativeLineEndings());
	}

	[Test]
	public void Select_NullableFieldThenEqualsNull_FiltersOnTheField()
	{
		var esql = CreateQuery<MetricDocument>()
			.From("metrics")
			.Select(m => m.Count)
			.Where(c => c == null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM metrics
            | KEEP count
            | WHERE count IS NULL
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldInQuerySyntaxAfterOrderBy_SortsThenKeeps()
	{
		var esql = (from l in CreateQuery<LogEntry>().From("logs-*")
					orderby l.Timestamp descending
					select l.Message)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT @timestamp DESC
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldInQuerySyntaxThenWhereOnTheValue_FiltersOnTheField()
	{
		var esql = (from m in
						from l in CreateQuery<LogEntry>().From("logs-*")
						select l.Message
					where m == "a"
					select m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "a"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_DatePart_GeneratesDateExtract()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Timestamp.Hour)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = DATE_EXTRACT("hour_of_day", @timestamp)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_DatePartThenWhere_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Timestamp.Hour)
			.Where(h => h > 8)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = DATE_EXTRACT("hour_of_day", @timestamp)
            | KEEP result
            | WHERE result > 8
            """.NativeLineEndings());
	}

	[Test]
	public void Select_StringLength_GeneratesLength()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message.Length)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = LENGTH(message)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenLengthInWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m.Length > 3)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE LENGTH(message) > 3
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedWithTheIndex_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select((l, i) => l.Duration * i)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void Select_SingleAggregationAfterGroupByThenWhere_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => g.Count())
			.Where(c => c > 5)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*g => new { Count = g.Count() }*");
	}

	[Test]
	public void Select_FieldWithMetadataThenObject_DoesNotKeepTheDroppedMetadata()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Id)
			.Select(l => l.Message)
			.Where(m => m == "a")
			.Select(m => new { M = m })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-* METADATA _id
            | KEEP message
            | WHERE message == "a"
            | RENAME message AS m
            | KEEP m
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedAfterWhereThenWhere_FiltersOnTheNewResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Where(x => x > 1)
			.Select(x => x * 2)
			.Where(y => y > 5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 1.0
            | EVAL result = (result * 2.0)
            | KEEP result
            | WHERE result > 5.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldAfterWhereThenComputedThenWhere_FiltersOnTheResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m == "ab")
			.Select(m => m.ToUpperInvariant())
			.Where(u => u == "AB")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "ab"
            | EVAL result = TO_UPPER(message)
            | KEEP result
            | WHERE result == "AB"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_GuidFieldThenWhere_FiltersOnTheField()
	{
		var id = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff");

		var esql = CreateQuery<ValueTypeDocument>()
			.From("values")
			.Select(d => d.Id)
			.Where(x => x == id)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM values
            | KEEP id
            | WHERE id == "6f9619ff-8b86-d011-b42d-00c04fc964ff"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_NullableGuidFieldThenEqualsNull_FiltersOnTheField()
	{
		var esql = CreateQuery<ValueTypeDocument>()
			.From("values")
			.Select(d => d.ParentId)
			.Where(x => x == null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM values
            | KEEP parentId
            | WHERE parentId IS NULL
            """.NativeLineEndings());
	}

	[Test]
	public void Select_GuidFieldThenOrderBy_SortsOnTheField()
	{
		var esql = CreateQuery<ValueTypeDocument>()
			.From("values")
			.Select(d => d.Id)
			.OrderBy(x => x)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM values
            | KEEP id
            | SORT id
            """.NativeLineEndings());
	}

	[Test]
	public void Select_TimeSpanFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<ValueTypeDocument>()
			.From("values")
			.Select(d => d.Elapsed)
			.Where(x => x > TimeSpan.FromSeconds(5))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM values
            | KEEP elapsed
            | WHERE elapsed > 5 seconds
            """.NativeLineEndings());
	}

	[Test]
	public void Select_DateOnlyFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<ValueTypeDocument>()
			.From("values")
			.Select(d => d.Day)
			.Where(x => x > new DateOnly(2024, 1, 1))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM values
            | KEEP day
            | WHERE day > "2024-01-01"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_DateOnlyFieldThenOrderByDescending_SortsOnTheField()
	{
		var esql = CreateQuery<ValueTypeDocument>()
			.From("values")
			.Select(d => d.Day)
			.OrderByDescending(x => x)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM values
            | KEEP day
            | SORT day DESC
            """.NativeLineEndings());
	}

	[Test]
	public void Select_TimeOnlyFieldThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<ValueTypeDocument>()
			.From("values")
			.Select(d => d.At)
			.Where(x => x > new TimeOnly(10, 30))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM values
            | KEEP at
            | WHERE at > "10:30:00"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenJoin_IsNotSupported()
	{
		var threats = CreateQuery<ThreatListEntry>().From("threat_list");

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.ClientIp)
			.Join(threats, ip => ip, t => t.ClientIp, (ip, t) => new { Ip = ip, t.ThreatLevel })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void Select_ComputedThenJoin_IsNotSupported()
	{
		var languages = CreateQuery<LanguageLookup>().From("languages_lookup");

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode / 100)
			.Join(languages, c => c, x => x.LanguageCode, (c, x) => new { x.LanguageName })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void Select_FieldThenWhereThenJoin_IsNotSupported()
	{
		var threats = CreateQuery<ThreatListEntry>().From("threat_list");

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.ClientIp)
			.Where(ip => ip != null)
			.Join(threats, ip => ip, t => t.ClientIp, (ip, t) => new { t.ThreatLevel })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void Select_FieldThenGroupByWithResultSelector_KeepsTheTranslationMessage()
	{
		// the result selector takes the key, not the row, so the value is not put in its place
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.GroupBy(m => m.Length, (length, messages) => length)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("GroupBy with a result selector is not supported*");
	}

	[Test]
	public void Select_FieldThenSelectMany_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.SelectMany(m => m)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*SelectMany*");
	}

	[Test]
	public void Select_FieldThenLeftJoin_IsNotSupported()
	{
		var threats = CreateQuery<ThreatListEntry>().From("threat_list");

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.ClientIp)
			.GroupJoin(threats, ip => ip, t => t.ClientIp, (ip, ts) => new { ip, ts })
			.SelectMany(x => x.ts.DefaultIfEmpty(), (x, t) => new { x.ip, t!.ThreatLevel })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void Select_FieldThenWhereThenIdentity_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m == "ab")
			.Select(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "ab"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhereThenIdentityThenWhere_FiltersOnTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m == "ab")
			.Select(m => m)
			.Where(m => m != "cd")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "ab"
            | WHERE message != "cd"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhereThenCast_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode)
			.Where(s => s > 1)
			.Select(s => (long)s)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode > 1
            | KEEP statusCode
            """.NativeLineEndings());
	}

	[Test]
	public void Select_RenamedFieldThenWhereThenIdentity_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Level)
			.Where(v => v == "ab")
			.Select(v => v)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP log.level
            | WHERE log.level == "ab"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_NestedFieldThenWhereThenIdentity_KeepsTheField()
	{
		var esql = CreateQuery<EagerNestedDocument>()
			.From("docs")
			.Select(d => d.Host.Name)
			.Where(n => n == "ab")
			.Select(n => n)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM docs
            | KEEP host.name
            | WHERE host.name == "ab"
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenOrderByThenTakeThenIdentity_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.OrderBy(m => m)
			.Take(3)
			.Select(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | SORT message
            | LIMIT 3
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenOrderByThenIdentity_KeepsTheSort()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.OrderBy(m => m)
			.Select(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | SORT message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenRawEsqlThenIdentity_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.RawEsql("LIMIT 10")
			.Select(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | LIMIT 10
            """.NativeLineEndings());
	}

	[Test]
	public void Select_NullableFieldThenWhereThenIdentity_KeepsTheField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => (int?)l.StatusCode)
			.Where(s => s > 1)
			.Select(s => s)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP statusCode
            | WHERE statusCode > 1
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhereThenIdentityToABaseType_KeepsTheSelect()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m == "ab")
			.Select<string, object>(m => m)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "ab"
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldThenWhereThenMemberOfTheValue_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(m => m == "ab")
			.Select(m => m.Length)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | WHERE message == "ab"
            | EVAL result = LENGTH(message)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldAfterForkThenObject_DoesNotKeepTheForkColumn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Fork(b => b.Where(l => l.IsError), b => b.Take(1))
			.Select(l => l.Message)
			.Where(m => m == "ab")
			.Select(m => new { M = m })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | FORK (WHERE isError) (LIMIT 1)
            | KEEP message
            | WHERE message == "ab"
            | RENAME message AS m
            | KEEP m
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldAfterForkWithMetadataThenObject_KeepsNeither()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Id)
			.Fork(b => b.Where(l => l.IsError), b => b.Take(1))
			.Select(l => l.Message)
			.Where(m => m == "ab")
			.Select(m => new { M = m })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-* METADATA _id
            | FORK (WHERE isError) (LIMIT 1)
            | KEEP message
            | WHERE message == "ab"
            | RENAME message AS m
            | KEEP m
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedOfTwoTypesInOneQuery_ReadsEachResult()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.StatusCode * 2)
			.Where(x => x > 1)
			.Select(x => x > 500)
			.Where(b => b)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (statusCode * 2)
            | KEEP result
            | WHERE result > 1
            | EVAL result = (result > 500)
            | KEEP result
            | WHERE result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedThenSkip_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Duration * 3)
			.Skip(2)
			.Where(x => x > 1)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*'Skip' is not directly supported*");
	}

	[Test]
	public void Select_FieldThenDistinct_IsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Distinct()
			.Where(m => m == "ab")
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*'Distinct' is not directly supported*");
	}
}
