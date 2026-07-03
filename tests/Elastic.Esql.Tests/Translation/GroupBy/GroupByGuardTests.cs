// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.GroupBy;

public class GroupByGuardTests : EsqlTestBase
{
	[Test]
	public void GroupBy_WithoutSelect_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*GroupBy*Select*");
	}

	[Test]
	public void GroupBy_FollowedByCount_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Count();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*GroupBy*Select*");
	}

	[Test]
	public void GroupBy_SelectWithComputedMember_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level.MultiField("keyword"))
			.Select(g => new { Level = g.Key, Rate = g.Count() / 100.0 })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*rate*");
	}
}
