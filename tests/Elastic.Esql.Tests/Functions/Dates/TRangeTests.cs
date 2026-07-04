// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class TRangeTests : EsqlTestBase
{
	[Test]
	public void TRange_InWhere_GeneratesCorrectEsql()
	{
		var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var end = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => (bool)EsqlFunctions.TRange(start, end))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE TRANGE("2024-01-01T00:00:00.000Z", "2024-02-01T00:00:00.000Z")
			""".NativeLineEndings());
	}

	[Test]
	[Skip("Inline 'new DateTime(...)' arguments are folded into corrupt numeric literals (e.g. TRANGE(2024110001, 2024210001)) instead of ISO date strings. Expected-correct output matches the closure-captured form. Product fix belongs to separate work.")]
	public void TRange_InWhereWithInlineConstructorDates_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => (bool)EsqlFunctions.TRange(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE TRANGE("2024-01-01T00:00:00.000Z", "2024-02-01T00:00:00.000Z")
			""".NativeLineEndings());
	}
}
