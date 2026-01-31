// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class CapturedVariableTests : EsqlTestBase
{
	[Test]
	public void Where_CapturedIntVariable_UsesValue()
	{
		var threshold = 500;

		var esql = Client.Query<LogEntry>()
			.Where(l => l.StatusCode >= threshold)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            """);
	}

	[Test]
	public void Where_CapturedStringVariable_UsesValue()
	{
		var level = "ERROR";

		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == level)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            """);
	}

	[Test]
	public void Where_CapturedDateTime_UsesValue()
	{
		var cutoff = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		var esql = Client.Query<LogEntry>()
			.Where(l => l.Timestamp > cutoff)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE @timestamp > "2024-01-01T00:00:00.000Z"
            """);
	}
}
