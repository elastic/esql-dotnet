// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.GroupBy;

public class BucketTests : EsqlTestBase
{
	[Test]
	public void Bucket_IntegerBuckets_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => EsqlFunctions.Bucket(l.Duration, 10))
			.Select(g => new { Bucket = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY bucket = BUCKET(duration, 10)
            """);
	}

	[Test]
	public void Bucket_StringSpan_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => EsqlFunctions.Bucket(l.Duration, "100"))
			.Select(g => new { Bucket = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY bucket = BUCKET(duration, "100")
            """);
	}

	[Test]
	public void TBucket_GeneratesCorrectEsql()
	{
		var esql = Client.Query<LogEntry>()
			.GroupBy(l => EsqlFunctions.TBucket(l.Timestamp, "1 hour"))
			.Select(g => new { Bucket = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY bucket = TBUCKET(@timestamp, "1 hour")
            """);
	}
}
