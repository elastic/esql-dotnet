// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.TypeMapping.FieldNameResolution;

public class CamelCaseTests : EsqlTestBase
{
	[Test]
	public void Property_NoAttribute_UsesCamelCase()
	{
		// LogEntry.Message has no attribute, should become "message"
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Message == "test")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message.keyword == "test"
            """);
	}

	[Test]
	public void Property_StatusCode_UsesCamelCase()
	{
		// LogEntry.StatusCode should become "statusCode"
		var esql = Client.Query<LogEntry>()
			.Where(l => l.StatusCode == 200)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode == 200
            """);
	}

	[Test]
	public void Property_Duration_UsesCamelCase()
	{
		// LogEntry.Duration should become "duration"
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Duration > 1000)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE duration > 1000
            """);
	}
}
