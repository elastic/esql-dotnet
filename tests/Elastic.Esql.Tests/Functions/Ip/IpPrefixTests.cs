// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Ip;

public class IpPrefixTests : EsqlTestBase
{
	[Test]
	public void IpPrefix_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Prefix = EsqlFunctions.IpPrefix(l.ClientIp!, 24, 4) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL prefix = IP_PREFIX(clientIp, 24, 4)
            | KEEP prefix
            """.NativeLineEndings());
	}

	[Test]
	public void IpPrefix_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.IpPrefix(l.ClientIp!.MultiField("keyword"), 24, 4) == "10.0.0.0")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE IP_PREFIX(clientIp.keyword, 24, 4) == "10.0.0.0"
            """.NativeLineEndings());
	}
}
