// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class GroupBySelectSelectTests : EsqlTestBase
{
	[Test]
	public void GroupBy_SelectThenSelect_DoesNotMergeAcrossStats()
	{
		var esql = CreateQuery<SimpleDocument>()
			.From("docs-*")
			.GroupBy(d => d.Name)
			.Select(g => new { Name = g.Key, Count = g.Count() })
			.Select(x => new { x.Count })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| STATS count = COUNT(*) BY name
			| KEEP count
			""".NativeLineEndings());
	}
}
