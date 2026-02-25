// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class NullableProjectionTests : EsqlTestBase
{
	// ============================================================================
	// Nullable cast (no-op unwrap)
	// ============================================================================

	[Test]
	public void Select_NullableCast_ValueType_Renamed_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Code = (int?)l.StatusCode })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME statusCode AS code
			| KEEP code
			""".NativeLineEndings());
	}

	[Test]
	public void Select_NullableCast_SameFieldName_GeneratesKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { StatusCode = (int?)l.StatusCode })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP statusCode
			""".NativeLineEndings());
	}

	[Test]
	public void Select_NullableCast_Double_GeneratesKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Duration = (double?)l.Duration })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP duration
			""".NativeLineEndings());
	}

	[Test]
	public void Select_NullableCast_WithMultipleFields_GeneratesKeepAndEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, StatusCode = (int?)l.StatusCode, Duration = (double?)l.Duration })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, statusCode, duration
			""".NativeLineEndings());
	}
}
