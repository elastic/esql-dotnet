// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class NestedAnonymousProjectionTests : EsqlTestBase
{
	[Test]
	public void Select_NestedAnonymousProjection_FlattensToDottedTarget()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { A = new { B = l.Message } })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME message AS a.b
			| KEEP a.b
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_NestedAnonymousScalarAccess_MergesToDirectField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { A = new { B = l.Message } })
			.Select(x => x.A.B)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_DeepNestedAnonymousScalarAccess_MergesRecursively()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { A = new { B = new { C = l.Message } } })
			.Select(x => x.A.B.C)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void Where_AfterNestedAnonymousProjection_UsesDottedFieldPath()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { A = new { B = l.StatusCode } })
			.Where(x => x.A.B > 200)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME statusCode AS a.b
			| KEEP a.b
			| WHERE a.b > 200
			""".NativeLineEndings());
	}

	[Test]
	public void OrderBy_AfterNestedAnonymousProjection_UsesDottedFieldPath()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { A = new { B = l.StatusCode } })
			.OrderBy(x => x.A.B)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME statusCode AS a.b
			| KEEP a.b
			| SORT a.b
			""".NativeLineEndings());
	}

	[Test]
	public void GroupBy_AfterNestedAnonymousProjection_UsesDottedFieldPath()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { A = new { B = l.Level } })
			.GroupBy(x => x.A.B)
			.Select(g => new { g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS a.b
			| KEEP a.b
			| STATS count = COUNT(*) BY key = a.b
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_ResultSelectorWithNestedAnonymousShapes_FlattensToDottedTargets()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { Outer = new { outer.Message }, Inner = new { inner!.LanguageName } }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| RENAME message AS outer.message, languageName AS inner.languageName
			| KEEP outer.message, inner.languageName
			""".NativeLineEndings());
	}
}
