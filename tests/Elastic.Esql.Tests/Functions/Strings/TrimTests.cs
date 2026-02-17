// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class TrimTests : EsqlTestBase
{
	[Test]

	public void Trim_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { TrimmedMessage = EsqlFunctions.Trim(l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL trimmedMessage = TRIM(message)
            """.NativeLineEndings());
	}
}
