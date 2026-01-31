// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.TypeMapping.FieldNameResolution;

public class AttributeTests : EsqlTestBase
{
	[Test]
	public void EsqlField_CustomName_GeneratesCorrectField()
	{
		// LogEntry.Level has [EsqlField("log.level")]
		var esql = Client.Query<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level == "ERROR"
            """);
	}

	[Test]
	public void EsqlField_Timestamp_GeneratesCorrectField()
	{
		// LogEntry.Timestamp has [EsqlField("@timestamp")]
		var esql = Client.Query<LogEntry>()
			.OrderBy(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT @timestamp
            """);
	}

	[Test]
	public void EsqlIndex_Attribute_GeneratesCorrectFrom()
	{
		// LogEntry has [EsqlIndex("logs-*")]
		var esql = Client.Query<LogEntry>()
			.ToString();

		_ = esql.Should().Be("FROM logs-*");
	}
}
