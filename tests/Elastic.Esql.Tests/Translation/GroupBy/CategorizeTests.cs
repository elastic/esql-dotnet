// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.GroupBy;

public class CategorizeTests : EsqlTestBase
{
	[Test]
	public void Categorize_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => EsqlFunctions.Categorize(l.Message.MultiField("keyword")))
			.Select(g => new { Category = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | STATS count = COUNT(*) BY category = CATEGORIZE(message.keyword)
            """.NativeLineEndings());
	}
}
