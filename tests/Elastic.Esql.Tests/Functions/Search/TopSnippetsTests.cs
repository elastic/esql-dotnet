// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Search;

public class TopSnippetsTests : EsqlTestBase
{
	[Test]
	public void TopSnippets_InSelect_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Snippet = EsqlFunctions.TopSnippets(l.Message, 3) })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL snippet = TOP_SNIPPETS(message, 3)
			| KEEP snippet
			""".NativeLineEndings());
	}
}
