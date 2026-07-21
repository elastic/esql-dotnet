// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.TypeMapping.Escaping;

public class RowColumnEscapingTests : EsqlTestBase
{
	[Test]
	public void Row_ReservedKeywordColumnName_EscapesWithBackticks()
	{
		var esql = CreateQuery<LogEntry>()
			.Row(() => new { Like = 1 })
			.ToString();

		_ = esql.Should().Be("ROW `Like` = 1");
	}

	[Test]
	public void Row_PlainColumnName_StaysUnquoted()
	{
		var esql = CreateQuery<LogEntry>()
			.Row(() => new { Answer = 42 })
			.ToString();

		_ = esql.Should().Be("ROW Answer = 42");
	}
}
