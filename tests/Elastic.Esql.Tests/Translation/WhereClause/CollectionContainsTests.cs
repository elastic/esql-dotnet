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
	public void Where_HashSetContains_GeneratesInClause()
	{
		var levels = new SortedSet<string> { "ERROR", "FATAL" };

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
	public void Where_ISetContains_GeneratesInClause()
	{
		var levels = (ISet<string>)new SortedSet<string> { "ERROR", "FATAL" };

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
}
