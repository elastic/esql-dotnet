// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class ConcatTests : EsqlTestBase
{
	[Test]

	public void Concat_TwoFields_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Combined = EsqlFunctions.Concat(l.Level, l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL combined = CONCAT(log.level, message)
            | KEEP combined
            """.NativeLineEndings());
	}
}
