// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

/// <summary>
/// The rows of an ES|QL query have no position, so a Select that reads the element index is refused, while one that
/// only declares it is translated as the Select without it.
/// </summary>
public class IndexedSelectTests : EsqlTestBase
{
	private static IQueryable<LogEntry> Logs() => CreateQuery<LogEntry>().From("logs-*");

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
}
