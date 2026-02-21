// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.OrderBy;

public class SortTests : EsqlTestBase
{
	[Test]
	public void OrderBy_SingleField_GeneratesSort()
	{
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
	public void OrderByDescending_SingleField_GeneratesSortDesc()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderByDescending(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT @timestamp DESC
            """.NativeLineEndings());
	}

	[Test]
	public void OrderBy_ThenBy_GeneratesMultipleSort()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Level.MultiField("keyword"))
			.ThenBy(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT log.level.keyword
            | SORT @timestamp
            """.NativeLineEndings());
	}

	[Test]
	public void OrderBy_ThenByDescending_GeneratesMultipleSort()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Level.MultiField("keyword"))
			.ThenByDescending(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT log.level.keyword
            | SORT @timestamp DESC
            """.NativeLineEndings());
	}
}
