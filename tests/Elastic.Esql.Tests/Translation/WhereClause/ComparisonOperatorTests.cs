// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class ComparisonOperatorTests : EsqlTestBase
{
	[Test]
	public void Where_LessThanOperator_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode < 400)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode < 400
            """.NativeLineEndings());
	}

	[Test]
	public void Where_LessThanOrEqualOperator_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode <= 399)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode <= 399
            """.NativeLineEndings());
	}

	[Test]
	public void Where_GreaterThanOperator_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration > 1000.0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE duration > 1000
            """.NativeLineEndings());
	}

	[Test]
	public void Where_GreaterThanOrEqualOperator_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration >= 500.5)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE duration >= 500.5
            """.NativeLineEndings());
	}
}
