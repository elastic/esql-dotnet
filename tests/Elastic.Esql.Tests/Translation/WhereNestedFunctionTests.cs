// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

/// <summary>Pins WHERE output for nested function arguments translated through sub-expression handling.</summary>
public class WhereNestedFunctionTests : EsqlTestBase
{
	[Test]
	public void Where_NestedMathFunctions_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Round(EsqlFunctions.Abs(l.Duration), 2) > 100)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE ROUND(ABS(duration), 2) > 100
			""".NativeLineEndings());
	}

	[Test]
	public void Where_NestedFunctionsWithLogicalAnd_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Round(EsqlFunctions.Abs(l.Duration), 2) > 100 && l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (ROUND(ABS(duration), 2) > 100 AND statusCode >= 500)
			""".NativeLineEndings());
	}

	[Test]
	public void Where_NestedStringFunctions_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.ToUpper(EsqlFunctions.Trim(l.Message)) == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE TO_UPPER(TRIM(message)) == "ERROR"
			""".NativeLineEndings());
	}
}
