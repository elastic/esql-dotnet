// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Math;

public class TrigTests : EsqlTestBase
{
	[Test]
	public void Acos_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Acos(l.Duration) > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE ACOS(duration) > 0
            """);
	}

	[Test]
	public void Acos_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Acos(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = ACOS(duration)
            """);
	}

	[Test]
	public void MathAcos_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = System.Math.Acos(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = ACOS(duration)
            """);
	}

	[Test]
	public void MathAcos_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => System.Math.Acos(l.Duration) > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE ACOS(duration) > 0
            """);
	}

	[Test]
	public void Asin_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Asin(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = ASIN(duration)
            """);
	}

	[Test]
	public void Atan_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Atan(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = ATAN(duration)
            """);
	}

	[Test]
	public void Atan2_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Atan2(l.Duration, l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = ATAN2(duration, statusCode)
            """);
	}

	[Test]
	public void MathAtan2_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = System.Math.Atan2(l.Duration, l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = ATAN2(duration, statusCode)
            """);
	}

	[Test]
	public void Cos_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Cos(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = COS(duration)
            """);
	}

	[Test]
	public void Cosh_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Cosh(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = COSH(duration)
            """);
	}

	[Test]
	public void Sin_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Sin(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = SIN(duration)
            """);
	}

	[Test]
	public void Sinh_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Sinh(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = SINH(duration)
            """);
	}

	[Test]
	public void Tan_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Tan(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = TAN(duration)
            """);
	}

	[Test]
	public void Tanh_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Tanh(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = TANH(duration)
            """);
	}
}
