// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

/// <summary>A member-less GroupBy result selector is either one aggregation call or unsupported; it never defaults to COUNT(*).</summary>
public class GroupByScalarSelectorTests : EsqlTestBase
{
	[Test]
	public void GroupBy_ScalarCount_EmitsCountAggregation()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => g.Count())
			.ToString();

		_ = esql.Should().Contain("| STATS count = COUNT(*) BY log.level");
	}

	[Test]
	public void GroupBy_ScalarSum_EmitsSumAggregation()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => g.Sum(x => x.Duration))
			.ToString();

		_ = esql.Should().Contain("| STATS sum = SUM(duration) BY log.level");
	}

	[Test]
	public void GroupBy_KeyOnlySelector_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => g.Key)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*single aggregation call*");
	}
}
