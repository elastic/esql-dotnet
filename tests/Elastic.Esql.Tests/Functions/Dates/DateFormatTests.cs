// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Dates;

public class DateFormatTests : EsqlTestBase
{
	[Test]

	public void DateFormat_Iso_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { FormattedDate = EsqlFunctions.DateFormat(l.Timestamp, "yyyy-MM-dd") })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL formattedDate = DATE_FORMAT(@timestamp, "yyyy-MM-dd")
            """.NativeLineEndings());
	}
}
