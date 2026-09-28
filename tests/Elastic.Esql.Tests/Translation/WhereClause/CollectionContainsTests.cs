// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class CollectionContainsTests : EsqlTestBase
{
	[Test]
	public void Where_ListContains_GeneratesInClause()
	{
		var levels = new List<string> { "ERROR", "FATAL" };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword")))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword IN ("ERROR", "FATAL")
			""".NativeLineEndings());
	}

	[Test]
	public void Where_HashSetContains_ThrowsNotSupported()
	{
		// the set's comparer decides what it contains, which the emitted IN would not follow
		var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ERROR", "FATAL" };

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword")))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*HashSet*way of its own*");
	}

	[Test]
	public void Where_SortedSetContains_ThrowsNotSupported()
	{
		// a set built with the default comparer is refused all the same: telling it apart
		// would take reflection, and a SortedSet compares its strings by culture
		var levels = new SortedSet<string> { "ERROR", "FATAL" };

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword")))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*SortedSet*way of its own*");
	}

	[Test]
	public void Where_ISetContains_ThrowsNotSupported()
	{
		// the collection is judged by what it is, not by the interface it is typed as
		var levels = (ISet<string>)new SortedSet<string> { "ERROR", "FATAL" };

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword")))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*SortedSet*way of its own*");
	}

	[Test]
	public void Where_IReadOnlyListContains_GeneratesInClause()
	{
		var levels = (IReadOnlyList<string>)["ERROR", "FATAL"];

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword")))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword IN ("ERROR", "FATAL")
			""".NativeLineEndings());
	}

	[Test]
	public void Where_IReadOnlyCollectionContains_GeneratesInClause()
	{
		var levels = (IReadOnlyCollection<string>)["ERROR", "FATAL"];

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword")))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword IN ("ERROR", "FATAL")
			""".NativeLineEndings());
	}

	[Test]
	public void Where_ArrayContainsExtension_GeneratesInClause()
	{
		var levels = new[] { "ERROR", "FATAL" };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword")))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword IN ("ERROR", "FATAL")
			""".NativeLineEndings());
	}

	[Test]
	public void Where_Contains_EmptyCollection_GeneratesFalse()
	{
		var levels = Array.Empty<string>();

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword")))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE false
			""".NativeLineEndings());
	}

	[Test]
	public void Where_Contains_NullCollection_ThrowsArgumentNullException()
	{
		var levels = (HashSet<string>?)null;

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels!.Contains(l.Level.MultiField("keyword")))
			.ToString();

		var exception = act.Should().Throw<ArgumentNullException>().Which;
		_ = exception.ParamName.Should().Be("collection");
	}

	[Test]
	public void Where_ContainsWithAnEqualityComparer_ThrowsNotSupported()
	{
		// the comparer would compare "error" and "ERROR" as equal, which the IN emitted
		// for the store does not
		var levels = new[] { "ERROR", "FATAL" };

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword"), StringComparer.OrdinalIgnoreCase))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*equality comparer*");
	}

	[Test]
	public void Where_ContainsWithTheOrdinalComparer_GeneratesInClause()
	{
		// ordinal equality is the comparison Elasticsearch performs, so the comparer changes nothing
		var levels = new[] { "ERROR", "FATAL" };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword"), StringComparer.Ordinal))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword IN ("ERROR", "FATAL")
			""".NativeLineEndings());
	}

	[Test]
	public void Where_ContainsWithTheDefaultComparer_GeneratesInClause()
	{
		var levels = new[] { "ERROR", "FATAL" };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => levels.Contains(l.Level.MultiField("keyword"), EqualityComparer<string>.Default))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword IN ("ERROR", "FATAL")
			""".NativeLineEndings());
	}
}
