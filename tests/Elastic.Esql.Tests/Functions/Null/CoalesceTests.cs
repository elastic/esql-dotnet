// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Null;

public class CoalesceTests : EsqlTestBase
{
	[Test]

	public void Coalesce_TwoFields_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.Select(l => new { Msg = EsqlFunctions.Coalesce(l.Message, "N/A") })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL msg = COALESCE(message, "N/A")
            """);
	}
}
