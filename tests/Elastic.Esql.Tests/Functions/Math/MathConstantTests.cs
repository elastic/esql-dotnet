// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Math;

public class MathConstantTests : EsqlTestBase
{
	[Test]
	public void E_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.E() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = E()
            """);
	}

	[Test]
	public void Pi_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Pi() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = PI()
            """);
	}

	[Test]
	public void Tau_EsqlFunction_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Val = EsqlFunctions.Tau() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL val = TAU()
            """);
	}

	// Note: Math.E, Math.PI, Math.Tau are const fields, so the C# compiler inlines their
	// values as numeric literals before expression trees are built. Use EsqlFunctions.E(),
	// EsqlFunctions.Pi(), EsqlFunctions.Tau() for the ES|QL function calls.
}
