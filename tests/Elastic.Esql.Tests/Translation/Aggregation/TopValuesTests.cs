// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.Aggregation;

public class TopValuesTests : EsqlTestBase
{
	[Test]
	public void Values_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				AllIps = EsqlFunctions.Values(g, l => l.ClientIp)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS allIps = VALUES(clientIp.keyword) BY level = log.level.keyword
            """);
	}

	[Test]
	public void First_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				FirstMsg = EsqlFunctions.First(g, l => l.Message)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS firstMsg = FIRST(message.keyword) BY level = log.level.keyword
            """);
	}

	[Test]
	public void Last_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				LastMsg = EsqlFunctions.Last(g, l => l.Message)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS lastMsg = LAST(message.keyword) BY level = log.level.keyword
            """);
	}

	[Test]
	public void Sample_InGroupBy_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				SampleMsg = EsqlFunctions.Sample(g, l => l.Message)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS sampleMsg = SAMPLE(message.keyword) BY level = log.level.keyword
            """);
	}
}
