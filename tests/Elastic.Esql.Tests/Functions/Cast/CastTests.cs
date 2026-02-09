// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Cast;

public class CastTests : EsqlTestBase
{
	[Test]
	public void CastToInteger_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.CastToInteger(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = duration::integer
            """);
	}

	[Test]
	public void CastToLong_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.CastToLong(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = duration::long
            """);
	}

	[Test]
	public void CastToDouble_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.CastToDouble(l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = statusCode::double
            """);
	}

	[Test]
	public void CastToBoolean_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.CastToBoolean(l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = statusCode::boolean
            """);
	}

	[Test]
	public void CastToKeyword_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.CastToKeyword(l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = statusCode::keyword
            """);
	}

	[Test]
	public void CastToInteger_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.CastToInteger(l.Duration) > 100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE duration::integer > 100
            """);
	}
}
