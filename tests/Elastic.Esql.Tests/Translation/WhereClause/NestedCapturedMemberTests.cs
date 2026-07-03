// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class NestedCapturedMemberTests : EsqlTestBase
{
	[Test]
	public void Where_ThreeLevelCapturedPath_InlinesValue()
	{
		var config = new { Filter = new { MinStatus = 400 } };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= config.Filter.MinStatus)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= 400
			""".NativeLineEndings());
	}

	[Test]
	public void Where_ThreeLevelCapturedPath_Parameterized_UsesLeafName()
	{
		var config = new { Filter = new { MinStatus = 400 } };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= config.Filter.MinStatus)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= ?MinStatus
			""".NativeLineEndings());
	}

	[Test]
	public void Where_TwoLevelCapturedPath_InlinesValue()
	{
		var config = new { MaxRetries = 3 };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode == config.MaxRetries)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode == 3
			""".NativeLineEndings());
	}
}
