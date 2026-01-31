// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Math;

public class AbsTests : EsqlTestBase
{
	[Test]
	public void Abs_InWhere_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => EsqlFunctions.Abs(l.Duration) > 100)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE ABS(duration) > 100
            """);
	}

	[Test]

	public void Abs_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { AbsDuration = EsqlFunctions.Abs(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL absDuration = ABS(duration)
            """);
	}
}
