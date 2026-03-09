// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class ChainedSelectTests : EsqlTestBase
{
	[Test]
	public void ChainedSelect_NarrowFields_SingleKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.StatusCode, l.Duration })
			.Select(x => new { x.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_NarrowToTwoFields_SingleKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.StatusCode, l.Duration })
			.Select(x => new { x.Message, x.StatusCode })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, statusCode
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_RenameChaining_SingleRename()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Severity = l.Level, l.Message })
			.Select(x => new { Sev = x.Severity, x.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS sev
			| KEEP message, sev
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_DeadRenameEliminated()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Severity = l.Level, l.Message })
			.Select(x => new { x.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_DeadEvalEliminated()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, Adjusted = l.StatusCode - 100 })
			.Select(x => new { x.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_EvalTargetRenamed()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, Adjusted = l.StatusCode - 100 })
			.Select(x => new { x.Message, Adj = x.Adjusted })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL adj = (statusCode - 100)
			| KEEP message, adj
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_ComputedFieldUsedInSubsequentComputation()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, Adjusted = l.StatusCode - 100 })
			.Select(x => new { x.Message, Doubled = x.Adjusted * 2 })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL doubled = ((statusCode - 100) * 2)
			| KEEP message, doubled
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_ThreeSelects_AllMerged()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.StatusCode, l.Duration })
			.Select(x => new { x.Message, x.StatusCode })
			.Select(y => new { y.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_ThreeSelects_WithRenameChaining()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Severity = l.Level, l.Message })
			.Select(x => new { Sev = x.Severity, x.Message })
			.Select(y => new { S = y.Sev, y.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS s
			| KEEP message, s
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_NonAnonymousInnerType_MergedCorrectly()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection
			{
				Level = l.Level,
				Message = l.Message,
				StatusCode = l.StatusCode
			})
			.Select(x => new { x.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_NonAnonymousOuterType_PreservesResultType()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Level, l.Message, l.StatusCode })
			.Select(x => new LogProjection { Level = x.Level, Message = x.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS log_level
			| KEEP message, log_level
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_NonAnonymousToNonAnonymous_RenameChained()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection { Level = l.Level, Message = l.Message })
			.Select(x => new { x.Level })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS level
			| KEEP level
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_AfterWhere_NotMergedAcrossWhere()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.StatusCode })
			.Where(x => x.StatusCode > 200)
			.Select(x => new { x.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, statusCode
			| WHERE statusCode > 200
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void ChainedSelect_SingleFieldAccess_NoMerge()
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
	public void ChainedSelect_WithEvalAndRename_CombinedCleanly()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new
			{
				Severity = l.Level,
				l.Message,
				Adjusted = l.StatusCode - 100
			})
			.Select(x => new { x.Message, x.Adjusted })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL adjusted = (statusCode - 100)
			| KEEP message, adjusted
			""".NativeLineEndings());
	}
}
