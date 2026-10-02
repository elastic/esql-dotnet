// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>
/// Ordering comparisons between strings, written in LINQ as CompareOrdinal against zero.
/// These are what keyset pagination needs when the tie-breaker is a text field.
/// </summary>
public class StringOrderingTests : EsqlTestBase
{
	[Test]
	public void Where_CompareOrdinalGreaterThanZero_TranslatesToGreaterThan()
	{
		// the ordering ES|QL applies to a keyword field is ordinal, so this is the form
		// that means exactly what the translation performs
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, "m") > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message > "m"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_CompareOrdinalOnConvertedProperty_ThrowsNotSupported()
	{
		// the field holds what the converter writes, and a converter is free to write
		// values in an order of its own, which the comparison would then report wrongly
		var query = CreateQuery<PrefixedCodeDocument>()
			.From("docs")
			.Where(d => string.CompareOrdinal(d.Code, "42") > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*JsonConverter*");
	}

	[Test]
	public void Where_CompareOrdinalLessThanOrEqualZero_TranslatesToLessThanOrEqual()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, "m") <= 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message <= "m"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_CompareWithOrdinal_TranslatesToTheSameComparison()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.Message, "m", StringComparison.Ordinal) > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message > "m"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ZeroOnTheLeft_FlipsTheOperator()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => 0 < string.CompareOrdinal(l.Message, "m"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message > "m"
            """.NativeLineEndings());
	}

	[Test]
	public void Where_KeysetPredicate_TranslatesAsAWhole()
	{
		// the shape keyset pagination produces when the tie-breaker is a text field
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration > 1.5 || (l.Duration == 1.5 && string.CompareOrdinal(l.Message, "m") > 0))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (duration > 1.5 OR (duration == 1.5 AND message > "m"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_CompareOverNullableField_SpellsOutTheMissingValue()
	{
		// string.Compare orders null before everything, where a comparison against a
		// missing field is null in ES|QL and the row would be dropped
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.ClientIp, "m", StringComparison.Ordinal) < 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (clientIp IS NULL OR clientIp < "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_CompareAboveNullableField_ExcludesTheMissingValue()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.ClientIp, "m", StringComparison.Ordinal) > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (clientIp IS NOT NULL AND clientIp > "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_NullableFieldOnTheRight_GuardsTheOtherWay()
	{
		// null sorts first, so "m" compared against a missing value is above it: the
		// row is out for "<" and in for ">", the mirror of the field on the left
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare("m", l.ClientIp, StringComparison.Ordinal) < 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (clientIp IS NOT NULL AND "m" < clientIp)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AboveNullableFieldOnTheRight_KeepsTheMissingValue()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare("m", l.ClientIp, StringComparison.Ordinal) > 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (clientIp IS NULL OR "m" > clientIp)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_PathThroughNullableParent_IsGuardedOnTheResolvedPath()
	{
		// Address can be missing while City cannot: a missing Address leaves the whole
		// path null, so the guard goes on the path as emitted
		var esql = CreateQuery<NullableNestedModel>()
			.From("people")
			.Where(m => string.Compare(m.Address!.City, "m", StringComparison.Ordinal) < 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM people
            | WHERE (address.city IS NULL OR address.city < "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ExpressionOfPathThroughNullableParent_ThrowsNotSupported()
	{
		var query = CreateQuery<NullableNestedModel>()
			.From("people")
			.Where(m => string.Compare(EsqlFunctions.Trim(m.Address!.City), "m", StringComparison.Ordinal) < 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*expression of a field*");
	}

	[Test]
	public void Where_CompareOrdinalBetweenTwoFields_ThrowsNotSupported()
	{
		// without a value to look at, nothing says whether the UTF-16 and UTF-8 orderings
		// agree on the comparison
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, l.ClientIp) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*two fields*");
	}

	[Test]
	public void Where_ValueAtOrAboveUE000_ThrowsNotSupported()
	{
		// a fullwidth letter sits at U+FF21: against it a supplementary character in the
		// field would sort one way in .NET and the other in Elasticsearch
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, "\uFF21") > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*U+E000*");
	}

	[Test]
	public void Where_SupplementaryCharacterInTheValue_ThrowsNotSupported()
	{
		var emoji = "\U0001F600";

		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, emoji) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*U+E000*");
	}

	[Test]
	public void Where_ExpressionOfNullableField_ThrowsNotSupported()
	{
		// the guard can spell out the ordering of a missing field, not of a function of
		// it, whose value for a missing field is not the field's null
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(EsqlFunctions.Trim(l.ClientIp!), "m", StringComparison.Ordinal) < 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*expression of a field*");
	}

	[Test]
	public void Where_ExpressionOfNonNullableField_IsStillTranslated()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(EsqlFunctions.Trim(l.Message), "m", StringComparison.Ordinal) < 0)
			.ToString();

		_ = esql.Should().Contain("TRIM(message) < ");
	}

	[Test]
	public void Where_ValueBelowUE000_TranslatesToComparison()
	{
		// CJK and Cyrillic sit well below U+E000, where the two orderings agree
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, "\u4E2D\u0416z") > 0)
			.ToString();

		_ = esql.Should().Contain("message > ");
	}

	[Test]
	public void Where_NullableFieldBehindMultiField_IsStillGuarded()
	{
		// the multi-field is missing whenever the field is: the guard reads the member's
		// nullability and names the path as emitted
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.ClientIp.MultiField("keyword"), "m", StringComparison.Ordinal) < 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (clientIp.keyword IS NULL OR clientIp.keyword < "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_NullableFieldFromTypeContext_IsStillGuarded()
	{
		// every member of OptionalDocument is nullable, so the compiler records that once
		// on the type and not on the member: the guard has to be found there too
		var esql = CreateQuery<OptionalDocument>()
			.From("docs")
			.Where(d => string.Compare(d.ClientIp, "m", StringComparison.Ordinal) < 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM docs
            | WHERE (clientIp IS NULL OR clientIp < "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_CompareAgainstTheProjectedRow_ComparesTheField()
	{
		// a projected scalar row is the field it was read from
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.ClientIp)
			.Where(s => string.Compare(s, "m", StringComparison.Ordinal) < 0)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | KEEP clientIp
            | WHERE (clientIp IS NULL OR clientIp < "m")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_NullableFieldBehindAnAnonymousType_IsStillGuarded()
	{
		// an anonymous type carries no nullable annotation, and the column is the same
		// column: a guard on one that is never null is a no-op, a missing one is not
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.ClientIp, l.Message })
			.Where(x => string.Compare(x.ClientIp, "m", StringComparison.Ordinal) < 0)
			.ToString();

		_ = esql.Should().Contain("(clientIp IS NULL OR clientIp < \"m\")");
	}

	[Test]
	public void Where_CompareOrdinalAgainstACapturedZero_TranslatesAsAgainstTheLiteral()
	{
		var zero = 0;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, "m") > zero)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE message > "m"
            """.NativeLineEndings());
	}
}
