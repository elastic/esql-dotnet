// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class NullIntermediateClosureTests : EsqlTestBase
{
	private sealed class FilterConfig
	{
		public FilterThresholds? Thresholds { get; set; }
	}

	private sealed class FilterThresholds
	{
		public int MinStatus { get; set; }
	}

	[Test]
	public void Where_NullIntermediateInComparisonChain_ThrowsWithMemberName()
	{
		var config = new FilterConfig();

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= config.Thresholds!.MinStatus)
			.ToString();

		_ = act.Should().Throw<InvalidOperationException>()
			.WithMessage("*MinStatus*evaluated to null*");
	}

	[Test]
	public void Where_NullIntermediateInEqualityChain_ThrowsWithMemberName()
	{
		var config = new FilterConfig();

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode == config.Thresholds!.MinStatus)
			.ToString();

		_ = act.Should().Throw<InvalidOperationException>()
			.WithMessage("*MinStatus*evaluated to null*");
	}
}
