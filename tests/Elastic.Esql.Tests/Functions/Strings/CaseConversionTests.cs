// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Strings;

public class CaseConversionTests : EsqlTestBase
{
	[Test]

	public void ToLower_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { LowerLevel = EsqlFunctions.ToLower(l.Level) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL lowerLevel = TO_LOWER(log.level)
            """.NativeLineEndings());
	}

	[Test]

	public void ToUpper_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { UpperLevel = EsqlFunctions.ToUpper(l.Level) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL upperLevel = TO_UPPER(log.level)
            """.NativeLineEndings());
	}
}
