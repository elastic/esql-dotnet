// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class KeepDropTests : EsqlTestBase
{
	[Test]
	public void Keep_WithStringFields_GeneratesKeep()
	{
		var esql = Client.Query<LogEntry>()
			.Keep("message", "statusCode")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message, statusCode
            """);
	}

	[Test]
	public void Drop_WithStringFields_GeneratesDrop()
	{
		var esql = Client.Query<LogEntry>()
			.Drop("message")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | DROP message
            """);
	}

	[Test]
	public void Keep_WithLambdaSelectors_GeneratesKeep()
	{
		var esql = Client.Query<LogEntry>()
			.Keep(l => l.Message, l => l.StatusCode)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message, statusCode
            """);
	}

	[Test]
	public void Keep_WithLambdaSelectors_ResolvesJsonPropertyName()
	{
		var esql = Client.Query<LogEntry>()
			.Keep(l => l.Timestamp, l => l.Level)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP @timestamp, log.level
            """);
	}

	[Test]
	public void Keep_WithProjection_SimpleFields_GeneratesKeep()
	{
		var esql = Client.Query<LogEntry>()
			.Keep(l => new { l.Message, l.StatusCode })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message, statusCode
            """);
	}

	[Test]
	public void Keep_WithProjection_RenameField_GeneratesEvalAndKeep()
	{
		var esql = Client.Query<LogEntry>()
			.Keep(l => new { Msg = l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL msg = message
            | KEEP msg
            """);
	}

	[Test]
	public void Keep_WithProjection_MixedFieldsAndRenames_GeneratesEvalAndKeep()
	{
		var esql = Client.Query<LogEntry>()
			.Keep(l => new { l.StatusCode, Msg = l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL msg = message
            | KEEP statusCode, msg
            """);
	}

	[Test]
	public void Keep_WithLambdaSelectors_AfterWhere_GeneratesKeep()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.StatusCode >= 500)
			.Keep(l => l.Message, l => l.StatusCode)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | KEEP message, statusCode
            """);
	}

	[Test]
	public void Keep_WithProjection_JsonPropertyNameRename_GeneratesEvalAndKeep()
	{
		// Timestamp maps to @timestamp via [JsonPropertyName], so selecting it as "Timestamp" is a rename
		var esql = Client.Query<LogEntry>()
			.Keep(l => new { l.Timestamp, l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL timestamp = @timestamp
            | KEEP message, timestamp
            """);
	}
}
