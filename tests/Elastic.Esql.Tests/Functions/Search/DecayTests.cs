// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Search;

public class DecayTests : EsqlTestBase
{
	[Test]
	public void Decay_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => EsqlFunctions.Decay("exp", l.ClientIp!, "10.0.0.0", "16") > 0.5)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE DECAY("exp", clientIp, "10.0.0.0", "16") > 0.5
			""".NativeLineEndings());
	}

	[Test]
	public void Decay_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Boost = EsqlFunctions.Decay("exp", l.ClientIp!, "10.0.0.0", "16") })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL boost = DECAY("exp", clientIp, "10.0.0.0", "16")
			| KEEP boost
			""".NativeLineEndings());
	}
}
