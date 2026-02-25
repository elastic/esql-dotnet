// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class ComputedFieldTests : EsqlTestBase
{
	[Test]
	public void Select_ComputedField_Multiplication_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Duration, DoubleDuration = l.Duration * 2 })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL doubleDuration = (duration * 2)
            | KEEP duration, doubleDuration
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedField_Subtraction_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Adjusted = l.StatusCode - 100 })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL adjusted = (statusCode - 100)
            | KEEP adjusted
            """.NativeLineEndings());
	}

	[Test]
	public void Select_ComputedField_Division_GeneratesEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { DurationSeconds = l.Duration / 1000 })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL durationSeconds = (duration / 1000)
            | KEEP durationSeconds
            """.NativeLineEndings());
	}

	[Test]
	public void Select_TernaryOperator_GeneratesCaseWhen()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Category = l.StatusCode >= 400 ? "Error" : "Success" })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL category = CASE WHEN (statusCode >= 400) THEN "Error" ELSE "Success" END
            | KEEP category
            """.NativeLineEndings());
	}
}
