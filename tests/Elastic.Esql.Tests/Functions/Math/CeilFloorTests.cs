// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Math;

public class CeilFloorTests : EsqlTestBase
{
	[Test]

	public void Ceil_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { CeiledDuration = EsqlFunctions.Ceil(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL ceiledDuration = CEIL(duration)
            """);
	}

	[Test]

	public void Floor_InSelect_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { FlooredDuration = EsqlFunctions.Floor(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL flooredDuration = FLOOR(duration)
            """);
	}
}
