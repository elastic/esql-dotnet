// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Ip;

public class CidrMatchTests : EsqlTestBase
{
	[Test]
	public void CidrMatch_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.CidrMatch(l.ClientIp!, "10.0.0.0/8"))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE CIDR_MATCH(clientIp, "10.0.0.0/8")
			""".NativeLineEndings());
	}
}
