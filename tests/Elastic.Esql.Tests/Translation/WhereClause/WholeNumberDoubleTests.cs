// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class WholeNumberDoubleTests : EsqlTestBase
{
	[Test]
	public void Where_DivisionByWholeDouble_KeepsDecimalPoint()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration / 100.0 > 1)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (duration / 100.0) > 1.0
			""".NativeLineEndings());
	}
}
