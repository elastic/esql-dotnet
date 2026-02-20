// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Search;

public class KqlQstrTests : EsqlTestBase
{
	[Test]
	public void Kql_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Kql("level:error AND message:timeout"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE KQL("level:error AND message:timeout")
            """.NativeLineEndings());
	}

	[Test]
	public void Qstr_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Qstr("message:error"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE QSTR("message:error")
            """.NativeLineEndings());
	}
}
