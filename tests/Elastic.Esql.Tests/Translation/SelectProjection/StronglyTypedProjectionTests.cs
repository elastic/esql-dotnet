// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class StronglyTypedProjectionTests : EsqlTestBase
{
	[Test]
	public void Select_StronglyTyped_SimpleFields_GeneratesKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection { Message = l.Message, Duration = l.Duration })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, duration
			""".NativeLineEndings());
	}

	[Test]
	public void Select_StronglyTyped_JsonPropertyName_GeneratesCorrectFieldNames()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection { Level = l.Level })
			.ToString();

		// LogProjection.Level has [JsonPropertyName("log_level")],
		// LogEntry.Level has [JsonPropertyName("log.level")]
		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS log_level
			| KEEP log_level
			""".NativeLineEndings());
	}

	[Test]
	public void Select_StronglyTyped_JsonPropertyName_StatusCode_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection { StatusCode = l.StatusCode })
			.ToString();

		// LogProjection.StatusCode has [JsonPropertyName("status")],
		// LogEntry.StatusCode resolves to "statusCode" via camelCase policy
		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME statusCode AS status
			| KEEP status
			""".NativeLineEndings());
	}

	[Test]
	public void Select_StronglyTyped_MixedFields_GeneratesKeepAndEval()
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
	public void Select_StronglyTyped_ComputedField_WithJsonPropertyName_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection { StatusCode = l.StatusCode - 100 })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL status = (statusCode - 100)
			| KEEP status
			""".NativeLineEndings());
	}

	[Test]
	public void Select_StronglyTyped_Ternary_WithJsonPropertyName_GeneratesCaseWhen()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new LogProjection { Level = l.StatusCode >= 400 ? "Error" : "OK" })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL log_level = CASE WHEN (statusCode >= 400) THEN "Error" ELSE "OK" END
			| KEEP log_level
			""".NativeLineEndings());
	}

	// ============================================================================
	// Constructor-call projections (records / classes)
	// ============================================================================

	[Test]
	public void Select_RecordConstructor_SimpleFields_GeneratesKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new RecordProjection(l.Message, l.StatusCode))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, statusCode
			""".NativeLineEndings());
	}

	[Test]
	public void Select_RecordConstructor_RenamedFields_GeneratesRename()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new RecordProjection(l.Level, l.StatusCode))
			.ToString();

		// LogEntry.Level → "log.level", RecordProjection.Message → "message"
		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS message
			| KEEP statusCode, message
			""".NativeLineEndings());
	}

	[Test]
	public void Select_RecordConstructor_ComputedField_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new RecordProjection(l.Message, l.StatusCode - 100))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL statusCode = (statusCode - 100)
			| KEEP message, statusCode
			""".NativeLineEndings());
	}

	[Test]
	public void Select_RecordConstructor_UnmatchedParameter_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new UnmatchedCtorProjection(l.Message))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*noSuchField*");
	}
}
