// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>
/// Any and All over a multi-value field with a predicate MATCH cannot answer, and the
/// way negation moves between the two. An ordering predicate such as "greater than" is
/// answered whole by MV_MIN and MV_MAX; a text predicate such as "starts with" reads
/// the field one position at a time, and needs the caller to state how many with
/// MultiValueLimit.
/// </summary>
public class MultiValueQuantifierTests : EsqlTestBase
{
	// how many positions these queries state with MultiValueLimit, one MV_SLICE each
	private const int Positions = 32;

	/// <summary>
	/// A field holding more values than the positions read is answered as null, which
	/// WHERE drops whether or not a NOT encloses it.
	/// </summary>
	private static string Bounded(string field, string positions, int limit = Positions) =>
		$"CASE(MV_COUNT({field}) > {limit}, NULL, ({positions}))";

	/// <summary>Any: the test holds at some position; an absent value does not count.</summary>
	private static string AnyOf(string field, Func<string, string> test, int limit = Positions) =>
		Bounded(field, string.Join(" OR ", Enumerable.Range(0, limit)
			.Select(position => $"COALESCE({test($"MV_SLICE({field}, {position}, {position})")}, false)")), limit);

	/// <summary>All: the test holds at every position that has a value.</summary>
	private static string AllOf(string field, Func<string, string> test, int limit = Positions) =>
		Bounded(field, string.Join(" AND ", Enumerable.Range(0, limit)
			.Select(position =>
			{
				var value = $"MV_SLICE({field}, {position}, {position})";
				return $"COALESCE({value} IS NULL OR {test(value)}, true)";
			})), limit);

	[Test]
	public void Any_StartsWith_MatchesSomeValue()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.StartsWith("wat")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AnyOf("tags", value => $"STARTS_WITH({value}, \"wat\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void Any_EndsWith_MatchesSomeValue()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.EndsWith("al")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AnyOf("tags", value => $"ENDS_WITH({value}, \"al\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void Any_Contains_MatchesSomeValue()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.Contains("at")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AnyOf("tags", value => $"{value} LIKE \"*at*\"")}}
            """.NativeLineEndings());
	}

	[Test]
	public void All_StartsWith_RequiresEveryValueToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.All(t => t.StartsWith("i")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AllOf("tags", value => $"STARTS_WITH({value}, \"i\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void Any_WithANegatedPredicate_BecomesNotAll()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => !t.StartsWith("i")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE NOT {{AllOf("tags", value => $"STARTS_WITH({value}, \"i\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void All_WithANegatedPredicate_BecomesNotAny()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.All(t => !t.Contains("at")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE NOT {{AnyOf("tags", value => $"{value} LIKE \"*at*\"")}}
            """.NativeLineEndings());
	}

	[Test]
	public void Any_NotEqual_BecomesNotAllEqual()
	{
		// "some value differs from x" is "not every value is x"
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t != "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (tags IS NULL OR (MV_COUNT(MV_DEDUPE(tags)) == 1 AND MATCH(tags, "iot")))
            """.NativeLineEndings());
	}

	[Test]
	public void NullGuardsOnTheElement_AreDropped()
	{
		// generated predicates guard the element against null; a stored value never is
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t != null && t.StartsWith("wat")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AnyOf("tags", value => $"STARTS_WITH({value}, \"wat\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void NegatedNullGuardedPredicate_IsReadThroughTheGuard()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => !(t != null && t.StartsWith("wat"))))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE NOT {{AllOf("tags", value => $"STARTS_WITH({value}, \"wat\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void Any_InAConstantList_TranslatesToMatchesInOr()
	{
		var wanted = new[] { "iot", "water" };

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (MATCH(tags, "iot") OR MATCH(tags, "water"))
            """.NativeLineEndings());
	}

	[Test]
	public void All_InAConstantList_RequiresEveryValueToBeListed()
	{
		var wanted = new[] { "iot", "water" };

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.All(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AllOf("tags", value => $"({value} == \"iot\" OR {value} == \"water\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void All_InAConstantListOfIntegers_ComparesTheirText()
	{
		var wanted = new[] { 5, 42 };

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Ratings.All(r => wanted.Contains(r)))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AllOf("ratings", value => $"(TO_STRING({value}) == \"5\" OR TO_STRING({value}) == \"42\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void ReservedCharactersInTheValue_AreEscaped()
	{
		// a quote would otherwise end the ES|QL string literal
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.StartsWith("a\"b")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AnyOf("tags", value => $"STARTS_WITH({value}, \"a\\\"b\")")}}
            """.NativeLineEndings());
	}

	[Test]
	public void AValueHoldingAControlCharacter_NeedsNoSpecialCase()
	{
		// the values are never joined, so no character of the data can be mistaken
		// for a delimiter
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.StartsWith("ab")))
			.ToString();

		_ = esql.Should().Contain("MV_SLICE(tags, 0, 0)");
	}

	[Test]
	public void Any_GreaterThan_ComparesTheLargestValue()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Ratings.Any(r => r > 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NOT NULL AND MV_MAX(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void All_GreaterThanOrEqual_ComparesTheSmallestValue()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Ratings.All(r => r >= 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NULL OR MV_MIN(ratings) >= 3)
            """.NativeLineEndings());
	}

	[Test]
	public void All_GreaterThan_ComparesTheSmallestValue()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Ratings.All(r => r > 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NULL OR MV_MIN(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void Any_LessThan_ComparesTheSmallestValue()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Ratings.Any(r => r < 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NOT NULL AND MV_MIN(ratings) < 3)
            """.NativeLineEndings());
	}

	[Test]
	public void AComparisonWithTheElementOnTheRight_IsFlipped()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Ratings.Any(r => 3 < r))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NOT NULL AND MV_MAX(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void ANegatedQuantifierStaysDefinedOverAMissingField()
	{
		// MV_MAX is null over a missing field, so the predicate has to say explicitly
		// that the field is present, or an enclosing NOT would answer neither way
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Ratings.All(r => !(r > 3)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (ratings IS NOT NULL AND MV_MAX(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void ANegatedAnyOverAMissingFieldIsTrue()
	{
		// LINQ reads a missing field as an empty sequence: !Any() holds there
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => !p.Tags.Any())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT COALESCE(MV_COUNT(tags), 0) > 0
            """.NativeLineEndings());
	}

	[Test]
	public void TwoPredicatesUnderOneNegation_StayDefinite()
	{
		// every position answers true or false on its own, so the conjunction under
		// the NOT is definite without any enclosing guard
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => !(p.Tags.Any(t => t.StartsWith("a")) && p.Categories.Any(c => c.StartsWith("b"))))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE NOT ({{AnyOf("tags", value => $"STARTS_WITH({value}, \"a\")")}} AND {{AnyOf("categories", value => $"STARTS_WITH({value}, \"b\")")}})
            """.NativeLineEndings());
	}

	[Test]
	public void Any_WithAPredicateThatCannotBeTranslated_StillThrows()
	{
		// nothing approximate: an unknown predicate keeps the existing behaviour
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.Length > 3));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void AFieldBeyondTheInspectedPositions_IsAnsweredAsNull()
	{
		// The predicate reads a fixed number of positions, so a field holding more values
		// cannot be answered from them. Saying "false" would let an enclosing NOT turn it
		// into a match, so the answer is null, which WHERE drops either way.
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.StartsWith("wat")))
			.ToString();

		_ = esql.Should().Contain($"CASE(MV_COUNT(tags) > {Positions}, NULL,");
	}

	[Test]
	public void ANegatedPredicateKeepsTheSameBound()
	{
		// the bound is inside the CASE, so the negation cannot turn it into a match
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => !p.Tags.Any(t => t.StartsWith("wat")))
			.ToString();

		_ = esql.Should().Contain($"NOT CASE(MV_COUNT(tags) > {Positions}, NULL,");
	}

	[Test]
	public void ControlCharactersInTheValue_AreEscaped()
	{
		// a raw newline in the query text is what the repository's own formatter exists
		// to avoid, and scalar predicates already go through it
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.StartsWith("a\nb")))
			.ToString();

		_ = esql.Should().Contain(@"""a\nb""");
	}

	[Test]
	public void AQuoteInAContainsValue_IsEscapedOnce()
	{
		// the pattern escapes the LIKE wildcards and the literal escapes the quote:
		// escaping the quote in both places would emit one backslash too many
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.Contains("a\"b")))
			.ToString();

		_ = esql.Should().Contain(@"LIKE ""*a\""b*""");
	}

	[Test]
	public void ACapturedValue_BecomesAParameter()
	{
		// with inlineParameters off the established scalar path emits ?name, and these
		// predicates have to do the same rather than embedding the value
		var tag = "iot";

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t == tag))
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Contain("MATCH(tags, ?tag)");
	}

	[Test]
	public void ACapturedValue_IsOneParameterForAllPositions()
	{
		var prefix = "wat";

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.StartsWith(prefix)))
			.ToEsqlString(inlineParameters: false);

		// the same parameter at every position, not one per position
		_ = esql.Should().Contain("?prefix");
		_ = esql.Should().NotContain("?prefix1");
	}

	[Test]
	public void ALiteralValue_StaysInline()
	{
		// only a captured variable becomes a parameter; a literal written in the query
		// has no name to give it
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Contains("water"))
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Contain("MATCH(tags, \"water\")");
	}

	[Test]
	public void ACapturedContainsPattern_StaysInline()
	{
		// ES|QL takes a literal after LIKE and rejects a parameter there, unlike a
		// function argument; the captured value is escaped into the pattern instead
		var fragment = "ate";

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(Positions)
			.Where(p => p.Tags.Any(t => t.Contains(fragment)))
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Contain("LIKE \"*ate*\"");
		_ = esql.Should().NotContain("?fragment");
	}

	[Test]
	public void WithoutAStatedLimit_APredicateOverTheValues_IsRefused()
	{
		// how many positions to read is a contract about the data, which the caller
		// states; without it the shape is not translated rather than translated with a
		// number chosen here
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t.StartsWith("wat")));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*MultiValueLimit*");
	}

	[Test]
	public void WithoutAStatedLimit_EqualityStillGoesThroughMatch()
	{
		// MATCH and MV_MIN / MV_MAX read the whole field: no positions, no limit needed
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot") && p.Ratings.Any(r => r > 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (MATCH(tags, "iot") AND (ratings IS NOT NULL AND MV_MAX(ratings) > 3))
            """.NativeLineEndings());
	}

	[Test]
	public void TheStatedLimit_SetsThePositionsRead()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(2)
			.Where(p => p.Tags.Any(t => t.StartsWith("wat")))
			.ToString();

		_ = esql.Should().Be(
			$$"""
            FROM products
            | WHERE {{AnyOf("tags", value => $"STARTS_WITH({value}, \"wat\")", limit: 2)}}
            """.NativeLineEndings());
	}

	[Test]
	public void ALimitStatedAfterThePredicate_StillApplies()
	{
		// a statement about the data, not a step of the query: its place in the chain
		// does not matter
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t.StartsWith("wat")))
			.MultiValueLimit(2)
			.ToString();

		_ = esql.Should().Contain("CASE(MV_COUNT(tags) > 2, NULL,");
		_ = esql.Should().NotContain("MV_SLICE(tags, 2, 2)");
	}

	[Test]
	public void OfTwoLimits_EachPredicateReadsTheNearestPrecedingOne()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(2)
			.Where(p => p.Tags.Any(t => t.StartsWith("a")))
			.MultiValueLimit(3)
			.Where(p => p.Tags.Any(t => t.EndsWith("b")))
			.ToString();

		var first = esql.IndexOf("CASE(MV_COUNT(tags) > 2, NULL,", StringComparison.Ordinal);
		var second = esql.IndexOf("CASE(MV_COUNT(tags) > 3, NULL,", StringComparison.Ordinal);

		_ = first.Should().BePositive();
		_ = second.Should().BeGreaterThan(first);
	}

	[Test]
	public void OfTwoLimitsInARow_TheLaterOneWins()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(2)
			.MultiValueLimit(3)
			.Where(p => p.Tags.Any(t => t.StartsWith("a")))
			.ToString();

		_ = esql.Should().Contain("CASE(MV_COUNT(tags) > 3, NULL,");
		_ = esql.Should().NotContain("> 2");
	}

	[Test]
	public void TheStatedLimit_CarriesIntoAForkBranch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(2)
			.Fork(b => b.Where(p => p.Tags.Any(t => t.StartsWith("wat"))), b => b.Take(1))
			.ToString();

		_ = esql.Should().Contain("CASE(MV_COUNT(tags) > 2, NULL,");
	}

	[Test]
	public void ALimitAboveWhatElasticsearchParses_IsRefusedWhenStated()
	{
		// each position adds a level to the expression, and Elasticsearch parses 400
		var query = CreateQuery<TaggedProduct>().From("products");

		var act = () => query.MultiValueLimit(EsqlQueryableExtensions.MaxMultiValueLimit + 1);

		_ = act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Test]
	public void TheLargestLimit_IsAccepted()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(EsqlQueryableExtensions.MaxMultiValueLimit)
			.Where(p => p.Tags.Any(t => t.StartsWith("wat")))
			.ToString();

		_ = esql.Should().Contain($"CASE(MV_COUNT(tags) > {EsqlQueryableExtensions.MaxMultiValueLimit}, NULL,");
	}

	[Test]
	public void MoreValuesThanTheExpressionAllows_AreRefused()
	{
		// each value of an In is a MATCH in an OR chain, and each adds a level to the
		// expression Elasticsearch parses
		var wanted = Enumerable.Range(0, EsqlQueryableExtensions.MaxMultiValueLimit + 1).Select(i => $"t{i}").ToList();

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*at most*");
	}

	[Test]
	public void PositionsAndValuesTogether_AreBounded()
	{
		// under All the values are written out inside every position, so both count
		var wanted = Enumerable.Range(0, 100).Select(i => $"t{i}").ToList();

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.MultiValueLimit(200)
			.Where(p => p.Tags.All(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*together*");
	}

	[Test]
	public void ALimitBelowOne_IsRefusedWhenStated()
	{
		var query = CreateQuery<TaggedProduct>().From("products");

		var act = () => query.MultiValueLimit(0);

		_ = act.Should().Throw<ArgumentOutOfRangeException>();
	}
}
