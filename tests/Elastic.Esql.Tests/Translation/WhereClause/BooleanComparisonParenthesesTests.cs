// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class BooleanComparisonParenthesesTests : EsqlTestBase
{
	[Test]
	public void Where_ComparisonEqualsBooleanField_ParenthesizesComparison()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => (l.Duration > 1) == l.IsError)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (duration > 1) == isError
			""".NativeLineEndings());
	}

	[Test]
	public void Where_ComparisonNotEqualsComparison_ParenthesizesBothSides()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => (l.Duration > 1) != (l.StatusCode < 500))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (duration > 1) != (statusCode < 500)
			""".NativeLineEndings());
	}
}
