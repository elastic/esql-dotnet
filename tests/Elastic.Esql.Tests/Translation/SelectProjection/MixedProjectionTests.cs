// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Functions;

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class MixedProjectionTests : EsqlTestBase
{
	[Test]
	public void Select_Rename_Eval_Keep_AllThreeCommands()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { LogLevel = l.Level, l.Message, Adjusted = l.StatusCode - 100 })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS logLevel
			| EVAL adjusted = (statusCode - 100)
			| KEEP message, logLevel, adjusted
			""".NativeLineEndings());
	}

	[Test]
	public void Select_StronglyTyped_Rename_Eval_Keep_AllThreeCommands()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection
			{
				Level = l.Level,
				Message = l.Message,
				StatusCode = l.StatusCode - 100
			})
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS log_level
			| EVAL status = (statusCode - 100)
			| KEEP message, log_level, status
			""".NativeLineEndings());
	}

	[Test]
	public void Select_MultipleRenames_WithEval_AndKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection
			{
				Level = l.Level,
				Message = l.Message,
				StatusCode = l.StatusCode,
				Duration = l.Duration
			})
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS log_level, statusCode AS status
			| KEEP message, duration, log_level, status
			""".NativeLineEndings());
	}

	[Test]
	public void Select_StronglyTyped_Rename_Eval_Keep_WithDuration()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection
			{
				Level = l.Level,
				StatusCode = l.StatusCode - 100,
				Duration = l.Duration
			})
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS log_level
			| EVAL status = (statusCode - 100)
			| KEEP duration, log_level, status
			""".NativeLineEndings());
	}

	[Test]
	public void Select_RenamedField_UsedInEval_ReferencesNewName()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new
			{
				Severity = l.Level,
				Combined = EsqlFunctions.Concat(l.Level, l.Message)
			})
			.ToString();

		// log.level is renamed to severity, so the EVAL must reference 'severity' not 'log.level'
		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS severity
			| EVAL combined = CONCAT(severity, message)
			| KEEP severity, combined
			""".NativeLineEndings());
	}

	[Test]
	public void Select_EvalWithFunctionCall_AndKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.StatusCode, Combined = EsqlFunctions.Concat(l.Level, l.Message) })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL combined = CONCAT(log.level, message)
			| KEEP statusCode, combined
			""".NativeLineEndings());
	}

	[Test]
	public void Select_Rename_EvalWithTernary_Keep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Severity = l.Level, l.Message, IsHighStatus = l.StatusCode >= 400 ? "Yes" : "No" })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS severity
			| EVAL isHighStatus = CASE WHEN (statusCode >= 400) THEN "Yes" ELSE "No" END
			| KEEP message, severity, isHighStatus
			""".NativeLineEndings());
	}

	[Test]
	public void Select_MultipleEvals_NoRename_GeneratesEvalAndKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Adjusted = l.StatusCode - 100, DurationMs = l.Duration * 1000 })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL adjusted = (statusCode - 100), durationMs = (duration * 1000)
			| KEEP adjusted, durationMs
			""".NativeLineEndings());
	}

	[Test]
	public void Select_AllKeep_NoRenameOrEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.StatusCode, l.Duration })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, statusCode, duration
			""".NativeLineEndings());
	}

	[Test]
	public void Select_SingleRename_OnlyRenameAndKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Severity = l.Level })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS severity
			| KEEP severity
			""".NativeLineEndings());
	}

	[Test]
	public void Select_SingleEval_OnlyEvalAndKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { DoubleStatus = l.StatusCode * 2 })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL doubleStatus = (statusCode * 2)
			| KEEP doubleStatus
			""".NativeLineEndings());
	}

	[Test]
	public void Select_Rename_MultipleEvals_Keep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new
			{
				Severity = l.Level,
				Adjusted = l.StatusCode - 100,
				Category = l.StatusCode >= 500 ? "ServerError" : "Other"
			})
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS severity
			| EVAL adjusted = (statusCode - 100), category = CASE WHEN (statusCode >= 500) THEN "ServerError" ELSE "Other" END
			| KEEP severity, adjusted, category
			""".NativeLineEndings());
	}
}
