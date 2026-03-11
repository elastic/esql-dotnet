// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class RawEsqlTests : EsqlTestBase
{
	[Test]
	public void RawEsql_AppendsSingleFragment()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.RawEsql("WHERE statusCode >= 500")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_NormalizesLeadingPipe()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.RawEsql("| LIMIT 5")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | LIMIT 5
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_AppendsMultilineFragmentsInOrder()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.RawEsql(
				"""
                | WHERE statusCode >= 500
                | LIMIT 3
                """
			)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | LIMIT 3
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_TypeShift_ChangesElementType()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.RawEsql<LogEntry, MetricDocument>("KEEP message");

		_ = query.ElementType.Should().Be<MetricDocument>();
		_ = query.ToString().Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void RawEsql_AllowsSourceFragmentsForExpertUsage()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.RawEsql("FROM metrics-*")
			.RawEsql("ROW prompt = \"hello\"")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | FROM metrics-*
            | ROW prompt = "hello"
            """.NativeLineEndings());
	}
}
