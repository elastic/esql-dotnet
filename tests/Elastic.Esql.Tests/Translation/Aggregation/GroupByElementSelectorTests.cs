// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class GroupByElementSelectorTests : EsqlTestBase
{
	[Test]
	public void GroupBy_WithElementSelector_ParameterlessSum_UsesElementField()
	{
		var esql = CreateQuery<SimpleDocument>()
			.From("docs-*")
			.GroupBy(d => d.Name, d => d.Value)
			.Select(g => new { Name = g.Key, Total = g.Sum() })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| STATS total = SUM(value) BY name
			""".NativeLineEndings());
	}

	[Test]
	public void GroupBy_WithElementSelector_CountStaysCountStar()
	{
		var esql = CreateQuery<SimpleDocument>()
			.From("docs-*")
			.GroupBy(d => d.Name, d => d.Value)
			.Select(g => new { Name = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| STATS count = COUNT(*) BY name
			""".NativeLineEndings());
	}

	[Test]
	public void GroupBy_WithResultSelector_ThrowsNotSupported()
	{
		var query = CreateQuery<SimpleDocument>()
			.From("docs-*")
			.GroupBy(d => d.Name, (key, items) => new { Key = key, Count = items.Count() });

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*result selector*");
	}

	[Test]
	public void GroupBy_WithComplexElementSelector_ThrowsNotSupported()
	{
		var query = CreateQuery<SimpleDocument>()
			.From("docs-*")
			.GroupBy(d => d.Name, d => new { d.Value, d.CreatedAt })
			.Select(g => new { Name = g.Key, Count = g.Count() });

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*element selector*");
	}
}
