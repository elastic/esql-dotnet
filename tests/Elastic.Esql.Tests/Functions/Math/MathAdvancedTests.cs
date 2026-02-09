// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Math;

public class MathAdvancedTests : EsqlTestBase
{
	[Test]
	public void Cbrt_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Cbrt(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = CBRT(duration)
            """);
	}

	[Test]
	public void MathCbrt_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = System.Math.Cbrt(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = CBRT(duration)
            """);
	}

	[Test]
	public void Exp_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Exp(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = EXP(duration)
            """);
	}

	[Test]
	public void MathExp_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = System.Math.Exp(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = EXP(duration)
            """);
	}

	[Test]
	public void Hypot_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Hypot(l.Duration, l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = HYPOT(duration, statusCode)
            """);
	}

	[Test]
	public void Signum_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Signum(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = SIGNUM(duration)
            """);
	}

	[Test]
	public void MathSign_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = System.Math.Sign(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = SIGNUM(duration)
            """);
	}

	[Test]
	public void CopySign_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.CopySign(l.Duration, l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = COPY_SIGN(duration, statusCode)
            """);
	}

	[Test]
	public void ScaleB_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.ScaleB(l.Duration, 3) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = SCALB(duration, 3)
            """);
	}

	[Test]
	public void RoundTo_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.RoundTo(l.Duration, 2) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = ROUND_TO(duration, 2)
            """);
	}

	[Test]
	public void Exp_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Exp(l.Duration) > 100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE EXP(duration) > 100
            """);
	}

	[Test]
	public void MathExp_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => System.Math.Exp(l.Duration) > 100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE EXP(duration) > 100
            """);
	}
}
