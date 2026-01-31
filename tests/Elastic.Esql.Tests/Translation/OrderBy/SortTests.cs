// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.OrderBy;

public class SortTests : EsqlTestBase
{
	[Test]
	public void OrderBy_SingleField_GeneratesSort()
	{
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
	public void OrderByDescending_SingleField_GeneratesSortDesc()
	{
		var esql = Client.Query<LogEntry>()
			.OrderByDescending(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT @timestamp DESC
            """);
	}

	[Test]
	public void OrderBy_ThenBy_GeneratesMultipleSort()
	{
		var esql = Client.Query<LogEntry>()
			.OrderBy(l => l.Level)
			.ThenBy(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT log.level
            | SORT @timestamp
            """);
	}

	[Test]
	public void OrderBy_ThenByDescending_GeneratesMultipleSort()
	{
		var esql = Client.Query<LogEntry>()
			.OrderBy(l => l.Level)
			.ThenByDescending(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT log.level
            | SORT @timestamp DESC
            """);
	}
}
