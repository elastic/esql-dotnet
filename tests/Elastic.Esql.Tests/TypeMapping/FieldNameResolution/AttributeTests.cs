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
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == "ERROR")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE log.level.keyword == "ERROR"
            """.NativeLineEndings());
	}

	[Test]
	public void EsqlField_Timestamp_GeneratesCorrectField()
	{
		// LogEntry.Timestamp has [EsqlField("@timestamp")]
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT @timestamp
            """.NativeLineEndings());
	}

	[Test]
	public void EsqlIndex_Attribute_GeneratesCorrectFrom()
	{
		// LogEntry has [EsqlIndex("logs-*")]
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.ToString();

		_ = esql.Should().Be("FROM logs-*");
	}
}
