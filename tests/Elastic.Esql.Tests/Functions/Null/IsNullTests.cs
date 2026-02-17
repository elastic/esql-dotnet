// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Null;

public class IsNullTests : EsqlTestBase
{
	[Test]
	public void IsNull_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.IsNull(l.Message))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message IS NULL
            """.NativeLineEndings());
	}

	[Test]
	public void IsNotNull_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.IsNotNull(l.Message))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message IS NOT NULL
            """.NativeLineEndings());
	}
}
