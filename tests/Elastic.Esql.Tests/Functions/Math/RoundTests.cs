// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Math;

public class RoundTests : EsqlTestBase
{
	[Test]

	public void Round_NoDecimals_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { RoundedDuration = EsqlFunctions.Round(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = ROUND(duration)
            """);
	}

	[Test]

	public void Round_WithDecimals_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { RoundedDuration = EsqlFunctions.Round(l.Duration, 2) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = ROUND(duration, 2)
            """);
	}
}
