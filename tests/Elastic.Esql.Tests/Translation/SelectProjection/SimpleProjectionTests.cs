// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class SimpleProjectionTests : EsqlTestBase
{
	[Test]
	public void Select_SingleField_GeneratesKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            """.NativeLineEndings());
	}

	[Test]
	public void Select_AnonymousType_SimpleFields_GeneratesKeepAndEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Level, l.Message })
			.ToString();

		// Field with different source name (log.level vs level) uses EVAL for rename
		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | EVAL level = log.level
            """.NativeLineEndings());
	}

	[Test]
	public void Select_AnonymousType_RenamedField_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { LogLevel = l.Level, l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP message
            | EVAL logLevel = log.level
            """.NativeLineEndings());
	}
}
