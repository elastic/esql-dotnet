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
	public void OrderBy_ThenBy_GeneratesCombinedSort()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Level.MultiField("keyword"))
			.ThenBy(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT log.level.keyword, @timestamp
            """.NativeLineEndings());
	}

	[Test]
	public void OrderBy_ThenByDescending_GeneratesCombinedSort()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Level.MultiField("keyword"))
			.ThenByDescending(l => l.Timestamp)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT log.level.keyword, @timestamp DESC
            """.NativeLineEndings());
	}

	[Test]
	public void OrderBy_StringToLower_GeneratesFunctionSort()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Message.ToLowerInvariant())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT TO_LOWER(message)
            """.NativeLineEndings());
	}

	[Test]
	public void OrderBy_ComposedStringFunctions_GeneratesNestedFunctionSort()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Message.Trim().ToLowerInvariant())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT TO_LOWER(TRIM(message))
            """.NativeLineEndings());
	}
}
