// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.TypeMapping.Escaping;

public class ColumnNameEscapingTests : EsqlTestBase
{
	[Test]
	public void Where_FieldWithSpace_EscapesWithBackticks()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.Where(d => d.ResponseSize > 100)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE `response size` > 100
			""".NativeLineEndings());
	}

	[Test]
	public void Where_NestedPathWithSpecialSegments_EscapesPerSegment()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.Where(d => d.UserAgent.OsName == "Windows")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE `user-agent`.`os name` == "Windows"
			""".NativeLineEndings());
	}

	[Test]
	public void Where_MultiFieldOnSpecialPath_AppendsUnquotedSuffix()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.Where(d => d.UserAgent.OsName.MultiField("keyword") == "Windows")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE `user-agent`.`os name`.keyword == "Windows"
			""".NativeLineEndings());
	}

	[Test]
	public void OrderBy_SpecialCharacterFields_EscapesWithBackticks()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.OrderBy(d => d.ResponseSize)
			.ThenBy(d => d.UserAgent.OsName)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| SORT `response size`, `user-agent`.`os name`
			""".NativeLineEndings());
	}

	[Test]
	public void Keep_SpecialCharacterFields_EscapesWithBackticks()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.Keep(d => d.UserAgent.OsName, d => d.Message)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP `user-agent`.`os name`, message
			""".NativeLineEndings());
	}

	[Test]
	public void Keep_ObjectWithSpecialName_EscapesWildcardPrefix()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.Keep(d => d.UserAgent)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP `user-agent`.*
			""".NativeLineEndings());
	}

	[Test]
	public void Drop_SpecialCharacterField_EscapesWithBackticks()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.Drop(d => d.ResponseSize)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| DROP `response size`
			""".NativeLineEndings());
	}

	[Test]
	public void Select_ComputedFromSpecialField_EscapesInEval()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.Select(d => new { Doubled = d.ResponseSize * 2 })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL doubled = (`response size` * 2)
			| KEEP doubled
			""".NativeLineEndings());
	}

	[Test]
	public void GroupBy_SpecialCharacterKey_EscapesInStatsBy()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.GroupBy(d => d.UserAgent.OsName)
			.Select(g => new { Os = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS count = COUNT(*) BY os = `user-agent`.`os name`
			""".NativeLineEndings());
	}

	[Test]
	public void GroupBy_AggregationOverSpecialField_EscapesInAggregate()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.GroupBy(d => d.Message)
			.Select(g => new { Msg = g.Key, Total = g.Sum(d => d.ResponseSize) })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| STATS total = SUM(`response size`) BY msg = message
			""".NativeLineEndings());
	}

	[Test]
	public void Select_AliasedSpecialCharacterField_GeneratesEscapedRenameSource()
	{
		var esql = CreateQuery<SpecialCharacterDocument>()
			.From("logs-*")
			.Select(d => new { Ua = d.UserAgent.Version })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME `user-agent`.version AS ua
			| KEEP ua
			""".NativeLineEndings());
	}
}
