// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Math;

public class ClampTests : EsqlTestBase
{
	[Test]
	public void Clamp_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = EsqlFunctions.Clamp(l.Duration, 0, 100) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = CLAMP(duration, 0, 100)
            """.NativeLineEndings());
	}

	[Test]
	public void MathClamp_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Val = System.Math.Clamp(l.Duration, 0, 100) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = CLAMP(duration, 0, 100)
            """.NativeLineEndings());
	}

	[Test]
	public void Clamp_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Clamp(l.Duration, 0, 100) > 50)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE CLAMP(duration, 0, 100) > 50
            """.NativeLineEndings());
	}

	[Test]
	public void MathClamp_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => System.Math.Clamp(l.Duration, 0, 100) > 50)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE CLAMP(duration, 0, 100) > 50
            """.NativeLineEndings());
	}
}
