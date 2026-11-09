// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;

namespace Elastic.Esql.Tests.Translation.SelectProjection;

/// <summary>
/// The rows of an ES|QL query have no position, so a Select, Where or SelectMany whose lambda reads the element index is
/// refused, while one that only declares it is translated as the overload without it. The operators the translation
/// does not support keep its message, with or without the index.
/// </summary>
public class IndexedSelectTests : EsqlTestBase
{
	private static IQueryable<LogEntry> Logs() => CreateQuery<LogEntry>().From("logs-*");

	private static IQueryable<ThreatListEntry> Threats() => CreateQuery<ThreatListEntry>().From("threat_list");

	[Test]
	public void Select_FieldWithUnusedIndex_KeepsTheField()
	{
		var esql = Logs().Select((l, i) => l.Duration).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP duration
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedWithUnusedIndex_GeneratesEval()
	{
		var esql = Logs().Select((l, i) => l.Duration * 2).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 2.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ObjectWithUnusedIndex_KeepsTheMembers()
	{
		var esql = Logs().Select((l, i) => new { l.Message }).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_WithUnusedIndexAfterSingleValue_FoldsIntoIt()
	{
		var esql = Logs().Select(l => l.Duration).Select((x, i) => x * 2).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 2.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_WithUnusedIndexAfterObject_MergesIntoIt()
	{
		var esql = Logs().Select(l => new { l.Duration }).Select((x, i) => x.Duration * 2).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 2.0)
            | KEEP result
            """.NativeLineEndings());
	}

	[Test]
	public void Select_FieldWithUnusedIndexThenWhere_FiltersOnTheField()
	{
		var esql = Logs().Select((l, i) => l.Duration).Where(x => x > 1).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP duration
            | WHERE duration > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedReadingTheIndex_IsNotSupported()
	{
		var act = () => Logs().Select((l, i) => l.Duration * i).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*element index*");
	}

	[Test]
	public void Select_TheIndexAlone_IsNotSupported()
	{
		var act = () => Logs().Select((l, i) => i).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*element index*");
	}

	[Test]
	public void Select_ObjectReadingTheIndex_IsNotSupported()
	{
		var act = () => Logs().Select((l, i) => new { l.Message, Position = i }).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*element index*");
	}

	[Test]
	public void Select_ReadingTheIndexAfterSingleValue_IsNotSupported()
	{
		var act = () => Logs().Select(l => l.Duration).Select((x, i) => x + i).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*element index*");
	}

	[Test]
	public void Select_ReadingTheIndexAfterWhereAfterSingleValue_IsNotSupported()
	{
		var act = () => Logs().Select(l => l.Duration).Where(x => x > 1).Select((x, i) => x + i).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*element index*");
	}

	[Test]
	public void Select_ReadingTheIndexAfterObject_IsNotSupported()
	{
		var act = () => Logs().Select(l => new { l.Duration }).Select((x, i) => x.Duration + i).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*element index*");
	}

	[Test]
	public void Select_ObjectReadingTheIndexAfterObject_IsNotSupported()
	{
		var act = () => Logs().Select(l => new { l.Duration }).Select((x, i) => new { x.Duration, Position = i }).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*element index*");
	}

	[Test]
	public void Select_ToABaseTypeWithUnusedIndex_KeepsTheField()
	{
		var esql = Logs().Select<LogEntry, object>((l, i) => l.Message).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Where_WithUnusedIndex_FiltersAsWithoutIt()
	{
		var esql = Logs().Where((l, i) => l.Duration > 1).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE duration > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Where_WithUnusedIndexAfterOrderBy_FiltersAfterTheSort()
	{
		var esql = Logs().OrderBy(l => l.Duration).Where((l, i) => l.Duration > 1).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT duration
            | WHERE duration > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Where_WithUnusedIndexAfterField_FiltersOnTheField()
	{
		var esql = Logs().Select(l => l.Duration).Where((x, i) => x > 1).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP duration
            | WHERE duration > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Where_WithUnusedIndexAfterComputed_FiltersOnTheResult()
	{
		var esql = Logs().Select(l => l.Duration * 3).Where((x, i) => x > 1).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL result = (duration * 3.0)
            | KEEP result
            | WHERE result > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Where_WithUnusedIndexAfterFieldThenSelect_ReadsTheField()
	{
		var esql = Logs().Select(l => l.Duration).Where((x, i) => x > 1).Select(x => x * 2).ToString();

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
	public void Where_WithUnusedIndexAfterObject_FiltersOnTheMember()
	{
		var esql = Logs().Select(l => new { l.Duration }).Where((x, i) => x.Duration > 1).ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP duration
            | WHERE duration > 1.0
            """.NativeLineEndings());
	}

	[Test]
	public void Where_WithUnusedIndexOverRowsWithoutColumn_IsNotSupported()
	{
		var act = () => Logs().RawEsql<LogEntry, double>("EVAL x = duration | KEEP x").Where((x, i) => x > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where cannot read the single value*");
	}

	[Test]
	public void Where_WithUnusedIndexAfterGroupedSingleAggregation_IsNotSupported()
	{
		var act = () => Logs().GroupBy(l => l.Level).Select(g => g.Count()).Where((c, i) => c > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("A single aggregation after GroupBy cannot be followed by Where*");
	}

	[Test]
	public void Where_ReadingTheIndex_IsNotSupported()
	{
		var act = () => Logs().Where((l, i) => l.Duration > i).ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("Where with the element index is not supported: the rows of an ES|QL query have no position to number.");
	}

	[Test]
	public void Where_TheIndexAlone_IsNotSupported()
	{
		var act = () => Logs().Where((l, i) => i < 10).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where with the element index*");
	}

	[Test]
	public void Where_ReadingTheIndexBesideACapturedValue_IsNotSupported()
	{
		var limit = 3;

		var act = () => Logs().Where((l, i) => i < limit && l.Duration > 1).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where with the element index*");
	}

	[Test]
	public void Where_ReadingTheIndexAfterSingleValue_IsNotSupported()
	{
		var act = () => Logs().Select(l => l.Duration).Where((x, i) => x > i).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Where with the element index*");
	}

	[Test]
	public void SelectMany_LeftJoinWithUnusedIndex_GeneratesLookupJoin()
	{
		var esql = Logs()
			.GroupJoin(Threats(), l => l.ClientIp, t => t.ClientIp, (l, matches) => new { l, matches })
			.SelectMany((g, i) => g.matches.DefaultIfEmpty(), (g, t) => new { g.l.Message, t!.ThreatLevel })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | LOOKUP JOIN threat_list ON clientIp
            | KEEP message, threatLevel
            """.NativeLineEndings());
	}

	[Test]
	public void SelectMany_LeftJoinReadingTheIndex_IsNotSupported()
	{
		var act = () => Logs()
			.GroupJoin(Threats(), l => l.ClientIp, t => t.ClientIp, (l, matches) => new { l, matches })
			.SelectMany((g, i) => g.matches.Take(i).DefaultIfEmpty(), (g, t) => new { g.l.Message, t!.ThreatLevel })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("SelectMany with the element index*");
	}

	[Test]
	public void SelectManyWithoutResultSelector_ReadingTheIndex_KeepsTheTranslationMessage()
	{
		var act = () => Logs().SelectMany((l, i) => new[] { l.Message, i.ToString(CultureInfo.InvariantCulture) }).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("SelectMany is only supported as part of a left outer join*");
	}

	[Test]
	public void TakeWhile_ReadingTheIndex_KeepsTheTranslationMessage()
	{
		var act = () => Logs().TakeWhile((l, i) => l.Duration > i).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.TakeWhile' is not supported*");
	}

	[Test]
	public void SkipWhile_ReadingTheIndex_KeepsTheTranslationMessage()
	{
		var act = () => Logs().SkipWhile((l, i) => l.Duration > i).ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("Method 'Queryable.SkipWhile' is not supported*");
	}
}
