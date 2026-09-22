// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

/// <summary>RENAME removes its source, so an alias whose source is still needed must be emitted as an EVAL copy.</summary>
public class ProjectionAliasTests : EsqlTestBase
{
	[Test]
	public void Select_AliasOnly_EmitsRename()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Msg = l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME message AS msg
			| KEEP msg
			""".NativeLineEndings());
	}

	[Test]
	public void Select_SourceKeptNextToAlias_EmitsEvalInsteadOfRename()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, Msg = l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL msg = message
			| KEEP message, msg
			""".NativeLineEndings());
	}

	[Test]
	public void Select_TwoAliasesOfSameSource_EmitsEvals()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Primary = l.Message, Secondary = l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL primary = message, secondary = message
			| KEEP primary, secondary
			""".NativeLineEndings());
	}

	[Test]
	public void Select_AliasNextToUnrelatedField_KeepsRename()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.StatusCode, Msg = l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME message AS msg
			| KEEP statusCode, msg
			""".NativeLineEndings());
	}
}
