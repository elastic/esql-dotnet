// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Math;

public class RoundTests : EsqlTestBase
{
	[Test]

	public void Round_NoDecimals_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = EsqlFunctions.Round(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = ROUND(duration)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]

	public void Round_WithDecimals_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = EsqlFunctions.Round(l.Duration, 2) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = ROUND(duration, 2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_AwayFromZero_GeneratesRoundWithoutDecimals()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, MidpointRounding.AwayFromZero) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = ROUND(duration)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalsAndAwayFromZero_GeneratesRoundWithDecimals()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, 2, MidpointRounding.AwayFromZero) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = ROUND(duration, 2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_Default_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration - FLOOR(duration) == 0.5, FLOOR(duration) + ABS(FLOOR(duration) % 2), ROUND(duration))
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_Decimals_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, 2) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration * POW(10, 2) - FLOOR(duration * POW(10, 2)) == 0.5, FLOOR(duration * POW(10, 2)) + ABS(FLOOR(duration * POW(10, 2)) % 2), ROUND(duration * POW(10, 2))) / POW(10, 2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_ToEven_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, MidpointRounding.ToEven) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration - FLOOR(duration) == 0.5, FLOOR(duration) + ABS(FLOOR(duration) % 2), ROUND(duration))
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalsAndToEven_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, 2, MidpointRounding.ToEven) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration * POW(10, 2) - FLOOR(duration * POW(10, 2)) == 0.5, FLOOR(duration * POW(10, 2)) + ABS(FLOOR(duration * POW(10, 2)) % 2), ROUND(duration * POW(10, 2))) / POW(10, 2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_ToZero_Truncates()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, MidpointRounding.ToZero) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration >= 0, FLOOR(duration), CEIL(duration))
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalsAndToZero_Truncates()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, 2, MidpointRounding.ToZero) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration * POW(10, 2) >= 0, FLOOR(duration * POW(10, 2)), CEIL(duration * POW(10, 2))) / POW(10, 2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_ToNegativeInfinity_GeneratesFloor()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, MidpointRounding.ToNegativeInfinity) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = FLOOR(duration)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalsAndToNegativeInfinity_GeneratesFloor()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, 2, MidpointRounding.ToNegativeInfinity) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = FLOOR(duration * POW(10, 2)) / POW(10, 2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_ToPositiveInfinity_GeneratesCeil()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, MidpointRounding.ToPositiveInfinity) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CEIL(duration)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalsAndToPositiveInfinity_GeneratesCeil()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, 2, MidpointRounding.ToPositiveInfinity) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CEIL(duration * POW(10, 2)) / POW(10, 2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_SumWithDecimals_ScalesTheWholeSum()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration + l.StatusCode, 2) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE((duration + statusCode) * POW(10, 2) - FLOOR((duration + statusCode) * POW(10, 2)) == 0.5, FLOOR((duration + statusCode) * POW(10, 2)) + ABS(FLOOR((duration + statusCode) * POW(10, 2)) % 2), ROUND((duration + statusCode) * POW(10, 2))) / POW(10, 2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalsFromField_ScalesByField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration * POW(10, statusCode) - FLOOR(duration * POW(10, statusCode)) == 0.5, FLOOR(duration * POW(10, statusCode)) + ABS(FLOOR(duration * POW(10, statusCode)) % 2), ROUND(duration * POW(10, statusCode))) / POW(10, statusCode)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_NegativeDecimals_RoundsToTensAndHundreds()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, -2) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration * POW(10, -2) - FLOOR(duration * POW(10, -2)) == 0.5, FLOOR(duration * POW(10, -2)) + ABS(FLOOR(duration * POW(10, -2)) % 2), ROUND(duration * POW(10, -2))) / POW(10, -2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_NegativeDecimalsAndAwayFromZero_GeneratesRound()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, -2, MidpointRounding.AwayFromZero) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = ROUND(duration, -2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_ModeFromCast_UsesTheMode()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, (MidpointRounding)3) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = FLOOR(duration)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_InWhere_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => System.Math.Round(l.Duration) == 2)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE CASE(duration - FLOOR(duration) == 0.5, FLOOR(duration) + ABS(FLOOR(duration) % 2), ROUND(duration)) == 2.0
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_ModeFromVariable_IsNotSupported()
	{
		var mode = MidpointRounding.ToZero;

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, mode) })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void MathRound_Decimal_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedPrice = System.Math.Round(l.Price) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedPrice = CASE(price - FLOOR(price) == 0.5, FLOOR(price) + ABS(FLOOR(price) % 2), ROUND(price))
            | KEEP roundedPrice
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalWithDecimals_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedPrice = System.Math.Round(l.Price, 2) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedPrice = CASE(price * POW(10, 2) - FLOOR(price * POW(10, 2)) == 0.5, FLOOR(price * POW(10, 2)) + ABS(FLOOR(price * POW(10, 2)) % 2), ROUND(price * POW(10, 2))) / POW(10, 2)
            | KEEP roundedPrice
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalAndAwayFromZero_GeneratesRound()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedPrice = System.Math.Round(l.Price, 2, MidpointRounding.AwayFromZero) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedPrice = ROUND(price, 2)
            | KEEP roundedPrice
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_IntegerField_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedStatus = System.Math.Round((double)l.StatusCode) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedStatus = CASE(statusCode - FLOOR(statusCode) == 0.5, FLOOR(statusCode) + ABS(FLOOR(statusCode) % 2), ROUND(statusCode))
            | KEEP roundedStatus
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_FloatField_RoundsMidpointToEven()
	{
		var esql = CreateQuery<BookProjection>()
			.From("books")
			.Select(b => new { RoundedScore = System.Math.Round(b.Score) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM books
            | EVAL roundedScore = CASE(score - FLOOR(score) == 0.5, FLOOR(score) + ABS(FLOOR(score) % 2), ROUND(score))
            | KEEP roundedScore
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_InOrderBy_RoundsMidpointToEven()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(l => System.Math.Round(l.Duration))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | SORT CASE(duration - FLOOR(duration) == 0.5, FLOOR(duration) + ABS(FLOOR(duration) % 2), ROUND(duration))
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalsFromVariable_IsNotSupported()
	{
		var digits = 2;

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, digits) })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void MathRound_NegativeDecimalsAndToZero_Truncates()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, -2, MidpointRounding.ToZero) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CASE(duration * POW(10, -2) >= 0, FLOOR(duration * POW(10, -2)), CEIL(duration * POW(10, -2))) / POW(10, -2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_NegativeDecimalsAndToNegativeInfinity_GeneratesFloor()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, -2, MidpointRounding.ToNegativeInfinity) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = FLOOR(duration * POW(10, -2)) / POW(10, -2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_NegativeDecimalsAndToPositiveInfinity_GeneratesCeil()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, -2, MidpointRounding.ToPositiveInfinity) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = CEIL(duration * POW(10, -2)) / POW(10, -2)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}

	[Test]
	public void MathRound_DecimalsFromFieldAndAwayFromZero_GeneratesRound()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { RoundedDuration = System.Math.Round(l.Duration, l.StatusCode, MidpointRounding.AwayFromZero) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | EVAL roundedDuration = ROUND(duration, statusCode)
            | KEEP roundedDuration
            """.NativeLineEndings());
	}
}
