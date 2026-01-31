// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class NullCheckTests : EsqlTestBase
{
	[Test]
	public void Where_EqualsNull_GeneratesComparison()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.ClientIp == null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE clientIp == null
            """);
	}

	[Test]
	public void Where_NotEqualsNull_GeneratesComparison()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.ClientIp != null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE clientIp != null
            """);
	}
}
