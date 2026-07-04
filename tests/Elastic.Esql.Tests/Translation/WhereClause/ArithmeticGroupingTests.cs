// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class ArithmeticGroupingTests : EsqlTestBase
{
	[Test]
	public void Where_GroupedArithmetic_PreservesGrouping()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => (l.Duration + l.StatusCode) * 2 > 10)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE ((duration + statusCode) * 2.0) > 10.0
			""".NativeLineEndings());
	}

	[Test]
	public void Where_NestedSubtraction_PreservesGrouping()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration - (l.StatusCode - 5) > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (duration - (statusCode - 5)) > 0.0
			""".NativeLineEndings());
	}
}
