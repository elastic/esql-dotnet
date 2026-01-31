// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class NowTests : EsqlTestBase
{
	[Test]
	public void Now_InWhere_GeneratesNow()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > EsqlFunctions.Now())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > NOW()
            """);
	}

	[Test]
	public void Now_LessThan_GeneratesNow()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp < EsqlFunctions.Now())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp < NOW()
            """);
	}
}
