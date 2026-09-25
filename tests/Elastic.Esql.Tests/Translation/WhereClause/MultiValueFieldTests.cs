// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Text.Json;

using Elastic.Esql.Translation;

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>
/// Any, All and Contains over multi-value document fields, answered over the field as a
/// whole: MATCH for a value, MV_MIN and MV_MAX for an ordering, IS NOT NULL for Any(), with
/// none of the row duplication MV_EXPAND would introduce. A shape whose values cannot be
/// compared that way is refused with a message that says why.
/// </summary>
public class MultiValueFieldTests : EsqlTestBase
{
	[Test]
	public void Where_AnyWithEquality_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "water"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "water"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithReversedEquality_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => "water" == t))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "water"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsOnAFieldCollection_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("water"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "water"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithoutPredicate_TranslatesToIsNotNull()
	{
		// an empty array is stored as a missing field, so a field that is present holds at
		// least one value
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE tags IS NOT NULL
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyComparedWithFalse_TranslatesToItsNegation()
	{
		// Elasticsearch cannot parse "tags IS NOT NULL == false"
#pragma warning disable IDE0100 // the comparison with a boolean is the shape under test
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any() == false)
			.ToString();
#pragma warning restore IDE0100

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT tags IS NOT NULL
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsComparedWithTrue_TranslatesToThePredicate()
	{
		// MATCH cannot be an operand of ==, so the comparison is left out
#pragma warning disable IDE0100 // the comparison with a boolean is the shape under test
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot") == true)
			.ToString();
#pragma warning restore IDE0100

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_TrueNotEqualToContains_TranslatesToItsNegation()
	{
		// the predicate may sit on either side of the comparison
#pragma warning disable IDE0100 // the comparison with a boolean is the shape under test
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => true != p.Tags.Contains("iot"))
			.ToString();
#pragma warning restore IDE0100

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyComparedWithABooleanOfTheDocument_ThrowsNotSupported()
	{
		// a boolean known only when the query runs cannot pick the predicate or its negation
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any() == (p.Name.Length > 3));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Any over tags with a boolean*");
	}

	[Test]
	public void Where_AnyOnAListProperty_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Categories.Any(c => c == "pumps"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (categories IS NOT NULL AND MATCH(categories, "pumps"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithEqualityOnANumericList_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.Any(r => r == 42))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NOT NULL AND MATCH(ratings, 42))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_TwoAnyPredicates_CombineWithOr()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "water") || p.Tags.Any(t => t == "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE ((tags IS NOT NULL AND MATCH(tags, "water")) OR (tags IS NOT NULL AND MATCH(tags, "iot")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_TwoAnyPredicates_CombineWithAnd()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot") && p.Tags.Any(t => t == "industrial"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE ((tags IS NOT NULL AND MATCH(tags, "iot")) AND (tags IS NOT NULL AND MATCH(tags, "industrial")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_NegatedAny_TranslatesToNotMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => !p.Tags.Any(t => t == "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyCombinedWithAScalarPredicate_KeepsBoth()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "water") && p.Name == "pump")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE ((tags IS NOT NULL AND MATCH(tags, "water")) AND name == "pump")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithACapturedValue_Parameterizes()
	{
		var tag = "water";

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == tag));

		var esql = query.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, ?tag))
            """.NativeLineEndings());
		_ = query.GetParameters()!.Parameters["tag"].GetString().Should().Be("water");
	}

	[Test]
	public void Where_ContainsOverAConstantCollection_StillTranslatesToIn()
	{
		// the existing behaviour must not change: here the collection is the constant,
		// and the document field is the argument
		var names = new[] { "a", "b" };

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => names.Contains(p.Name))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE name IN ("a", "b")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AllWithEquality_RequiresASingleMatchingValue()
	{
		// a missing field has no value that differs, as All() over an empty sequence is true
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.All(t => t == "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NULL OR (MV_COUNT(MV_DEDUPE(tags)) == 1 AND MATCH(tags, "iot")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AllWithEqualityOnANumericList_RequiresASingleMatchingValue()
	{
		// a field holding [42, 42] has every value equal to 42, so it must match:
		// the count has to be over the distinct values, not the raw ones, because
		// only keyword fields are deduplicated by the index
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.All(r => r == 42))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NULL OR (MV_COUNT(MV_DEDUPE(ratings)) == 1 AND MATCH(ratings, 42)))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AllWithInequality_TranslatesToNotMatch()
	{
		// "every value differs from x" is "none of the values is x"
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.All(t => t != "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AllCombinedWithAScalarPredicate_KeepsBoth()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.All(t => t == "iot") && p.Name == "sensor")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE ((tags IS NULL OR (MV_COUNT(MV_DEDUPE(tags)) == 1 AND MATCH(tags, "iot"))) AND name == "sensor")
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverASet_TranslatesToMatch()
	{
		// no collection instance exists at translation time, so a set's comparer is as
		// invisible as a StringComparison on a scalar: the store's equality decides
		var esql = CreateQuery<SetTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsOverASet_TranslatesToMatch()
	{
		var esql = CreateQuery<SetTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsOverAFrozenSet_TranslatesToMatch()
	{
		var esql = CreateQuery<FrozenTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsOverAnImmutableArray_TranslatesToMatch()
	{
		var esql = CreateQuery<ImmutableTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyAndAllOverAnImmutableArray_TranslateToMatch()
	{
		// Any and All bind to ImmutableArrayExtensions rather than Enumerable
		var esql = CreateQuery<ImmutableTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot") && p.Tags.All(t => t != "water"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE ((tags IS NOT NULL AND MATCH(tags, "iot")) AND NOT (tags IS NOT NULL AND MATCH(tags, "water")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsOverACollection_TranslatesToMatch()
	{
		var esql = CreateQuery<CollectionTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsOverAnInterfaceTypedField_TranslatesToMatch()
	{
		var esql = CreateQuery<InterfaceTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithAPerValuePredicate_ThrowsNotSupported()
	{
		// StartsWith holds for one value at a time, which needs the field read position by
		// position, and no function reads the field that way
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t.StartsWith("wat", StringComparison.Ordinal)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*individual values*");
	}

	[Test]
	public void Where_FieldContainsWithAnEqualityComparer_ThrowsNotSupported()
	{
		// MATCH compares the way the field is indexed, which the comparer would not follow
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("IOT", StringComparer.OrdinalIgnoreCase));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*equality comparer*");
	}

	[Test]
	public void Where_AnyOverAContainsWithAnEqualityComparer_ThrowsNotSupported()
	{
		// the values would be matched the way the store compares them, not the way the
		// comparer does
		var wanted = new[] { "IOT", "WATER" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t, StringComparer.OrdinalIgnoreCase)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*equality comparer*");
	}

	[Test]
	public void Where_ContainsWithACapturedValue_Parameterizes()
	{
		var tag = "water";

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains(tag));

		var esql = query.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, ?tag))
            """.NativeLineEndings());
		_ = query.GetParameters()!.Parameters["tag"].GetString().Should().Be("water");
	}

	[Test]
	public void Where_ContainsWithAComputedValue_TranslatesItAsAScalarComparisonDoes()
	{
		// Contains renders its value as Any over equality does, and as a scalar comparison
		var tag = "IOT";

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains(tag.ToLowerInvariant()))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, TO_LOWER("IOT")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsWithALiteral_StaysInlineWithoutInlineParameters()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("water"))
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "water"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithAnOrdering_TranslatesToMvMax()
	{
		// some value is above 3 exactly when the largest is
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.Any(r => r > 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NOT NULL AND MV_MAX(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AllWithAnOrdering_TranslatesToMvMin()
	{
		// every value is above 3 exactly when the smallest is, and a missing field is an
		// empty sequence, over which All holds
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.All(r => r > 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NULL OR MV_MIN(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithAnOrderingBelow_TranslatesToMvMin()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.Any(r => r < 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NOT NULL AND MV_MIN(ratings) < 3)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AllWithAnOrderingAtMost_TranslatesToMvMax()
	{
		// every value is at most 4 when the largest is
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.All(r => r <= 4))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NULL OR MV_MAX(ratings) <= 4)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithTheValueOnTheLeft_KeepsTheFieldOnTheLeft()
	{
		// "3 <= r" is "r >= 3"
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.Any(r => 3 <= r))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NOT NULL AND MV_MAX(ratings) >= 3)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_NegatedAnyWithAnOrdering_HoldsForAMissingField()
	{
		// the explicit IS NOT NULL keeps the negation true for a missing field, where
		// !Any() over an empty sequence is true
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => !p.Ratings.Any(r => r > 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (ratings IS NOT NULL AND MV_MAX(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithANegatedOrdering_BecomesNotAll()
	{
		// Any(not P) is "not All(P)"
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.Any(r => !(r > 3)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (ratings IS NULL OR MV_MIN(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverACapturedArray_MatchesEachValue()
	{
		var wanted = new[] { "iot", "water" };

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND (MATCH(tags, "iot") OR MATCH(tags, "water")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverAnEmptyCapturedArray_TranslatesToFalse()
	{
		var wanted = Array.Empty<string>();

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE false
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverACapturedSet_ThrowsNotSupported()
	{
		// the set's comparer decides what it contains, which the emitted MATCH would not follow
		var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IOT" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*HashSet*way of its own*");
	}

	[Test]
	public void Where_AnyOverTooManyCapturedValues_ThrowsNotSupported()
	{
		var wanted = Enumerable.Range(0, 257).Select(i => $"tag{i}").ToArray();

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*257 values*");
	}

	[Test]
	public void Where_AnyWithANullGuardOnTheElement_DropsTheGuard()
	{
		// a stored value is never null, so the guard says nothing
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t != null && t == "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyComparingTheElementToNull_ThrowsNotSupported()
	{
		// a stored value is never null, and MATCH(tags, null) is not valid ES|QL
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == null));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*tags with null*");
	}

	[Test]
	public void Where_ContainsNullOnAField_ThrowsNotSupported()
	{
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains(null));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*tags with null*");
	}

	[Test]
	public void Where_ContainsWithAnotherField_ThrowsNotSupported()
	{
		// MATCH takes a constant, and Elasticsearch rejects a field there
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains(p.Name));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*tags with another field*");
	}

	[Test]
	public void Where_AnyComparingTheElementWithAnotherField_ThrowsNotSupported()
	{
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == p.Name));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*tags with another field*");
	}

	[Test]
	public void Where_AnyWithAnOrOfEqualities_ThrowsNotSupported()
	{
		// Any(a || b) is Any(a) || Any(b), which the message says
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot" || t == "water"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*OR inside the predicate over tags*");
	}

	[Test]
	public void Where_AnyOverAFunctionOfTheElement_ThrowsNotSupported()
	{
		// the length of each value is a test of one value at a time
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t.Length > 3));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*individual values of tags*");
	}

	[Test]
	public void Where_ContainsOverAFilteredField_ThrowsNotSupported()
	{
		// Where reads the field through an argument of a static call, so the source is the
		// field's and not a captured collection's
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Where(t => t != "").Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*p.Tags.Where*LINQ operator over its values*");
	}

	[Test]
	public void Where_AnyOverAProjectedCollection_ThrowsNotSupported()
	{
		// after Select(p => p.Categories) the row is the collection, with no field name of its own
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Select(p => p.Categories)
			.Where(categories => categories.Any());

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*projected row*");
	}

	[Test]
	public void Where_AnyStartingWithAField_ThrowsNotSupported()
	{
		// the test holds for one value at a time, whatever the value it is given
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t.StartsWith(p.Name, StringComparison.Ordinal)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*individual values of tags*");
	}

	[Test]
	public void Where_AnyBehindTransparentIdentifiers_StripsAllPrefixes()
	{
		var source = new[] { new { Outer = new { Outer = new TaggedProduct() } } }.AsQueryable();

		_ = Translate(source.Where(x => x.Outer.Outer.Tags.Any())).Should().Be("tags IS NOT NULL");
	}

	[Test]
	public void Where_AllBehindTransparentIdentifiers_StripsAllPrefixes()
	{
		var source = new[] { new { Outer = new { Outer = new TaggedProduct() } } }.AsQueryable();

		_ = Translate(source.Where(x => x.Outer.Outer.Ratings.All(r => r > 3)))
			.Should().Be("(ratings IS NULL OR MV_MIN(ratings) > 3)");
	}

	[Test]
	public void Where_ContainsBehindTransparentIdentifiers_StripsAllPrefixes()
	{
		var source = new[] { new { Outer = new { Outer = new TaggedProduct() } } }.AsQueryable();

		_ = Translate(source.Where(x => x.Outer.Outer.Tags.Contains("iot"))).Should().Be("(tags IS NOT NULL AND MATCH(tags, \"iot\"))");
	}

	[Test]
	public void Where_ContainsOverAPropertyWithAJsonConverter_ThrowsNotSupported()
	{
		// the field holds "TAG-iot", which MATCH(tags, "iot") would not find
		var query = CreateQuery<ConvertedTagsProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*JsonConverter*");
	}

	[Test]
	public void Where_AnyWithoutPredicateOverAPropertyWithAJsonConverter_ThrowsNotSupported()
	{
		// a converter need not write one value per element, so even whether the field holds
		// any does not follow from the collection
		var query = CreateQuery<ConvertedTagsProduct>()
			.From("products")
			.Where(p => p.Tags.Any());

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*one value per element*");
	}

	[Test]
	public void Where_AnyOverAPropertyWithAJsonConverter_ThrowsNotSupported()
	{
		var query = CreateQuery<ConvertedTagsProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*JsonConverter*");
	}

	[Test]
	public void Where_AnyWithInequality_BecomesNotAllEqual()
	{
		// Any(t != v) is "not every value is v"
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t != "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (tags IS NULL OR (MV_COUNT(MV_DEDUPE(tags)) == 1 AND MATCH(tags, "iot")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverACapturedList_MatchesEachValue()
	{
		var wanted = new List<string> { "iot", "water" };

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND (MATCH(tags, "iot") OR MATCH(tags, "water")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverAMembershipOfANarrowInteger_MatchesEachValue()
	{
		// a short is looked up as an int: "wanted.Contains(s)" is "wanted.Contains((int)s)"
		var wanted = new List<int> { 1, 2 };

		var esql = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Sizes.Any(s => wanted.Contains(s)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (sizes IS NOT NULL AND (MATCH(sizes, 1) OR MATCH(sizes, 2)))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverAMembershipOfAnEnumAsANumber_MatchesTheName()
	{
		// the numbers are turned back into the enum, which its converter writes by name
		var codes = new[] { 1 };

		var esql = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Grades.Any(g => codes.Contains((int)g)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (grades IS NOT NULL AND MATCH(grades, "High"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverACapturedLinqQuery_MatchesEachValue()
	{
		// a LINQ operator compares with default equality, and is enumerated once
		var tags = new[] { "io", "water" };
		var wanted = tags.Where(tag => tag.Length > 2);

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "water"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverACapturedSortedSet_ThrowsNotSupported()
	{
		// a sorted set carries a comparer of its own, and is refused before anything reads it
		var wanted = new SortedSet<string> { "iot" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*SortedSet*way of its own*");
	}

	[Test]
	public void Where_AnyOverACapturedReadOnlyCollection_MatchesEachValue()
	{
		// AsReadOnly wraps the list, whose Contains compares with default equality
		var wanted = new List<string> { "iot", "water" }.AsReadOnly();

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND (MATCH(tags, "iot") OR MATCH(tags, "water")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverACapturedIteratorMethod_MatchesEachValue()
	{
		// the type the compiler generates for the iterator only enumerates the values, which
		// Contains then compares with default equality
		var wanted = Wanted();

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND (MATCH(tags, "iot") OR MATCH(tags, "water")))
            """.NativeLineEndings());

		static IEnumerable<string> Wanted()
		{
			yield return "iot";
			yield return "water";
		}
	}

	[Test]
	public void Where_AnyOverACapturedCollectionExpression_MatchesEachValue()
	{
		// typed as an interface, the collection expression is a read-only type the compiler
		// generates over an array
		IEnumerable<string> wanted = ["iot", "water"];

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND (MATCH(tags, "iot") OR MATCH(tags, "water")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyOverACapturedArrayHoldingNull_ThrowsNotSupported()
	{
		// a stored value is never null, and MATCH(tags, null) is not valid ES|QL
		var wanted = new[] { "iot", null };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*tags with null*");
	}

	[Test]
	public void Where_AllOverACapturedArray_ThrowsNotSupported()
	{
		// MATCH answers whether some value is one of these, not whether every value is
		var wanted = new[] { "iot", "water" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.All(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*membership*every value of tags must pass*");
	}

	[Test]
	public void Where_AnyWithNegatedMembership_ThrowsNotSupported()
	{
		// "some value is not one of these" is "not every value is one of these"
		var wanted = new[] { "iot", "water" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => !wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*membership*some value must fail*");
	}

	[Test]
	public void Where_AMethodTakingAComparerInsideAny_IsNotTakenForContains()
	{
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => OwnPredicates.SameTag(t, StringComparer.Ordinal)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Enumerable.Any*");
	}

	[Test]
	public void Where_AnAnyOfYourOwn_IsNotTranslated()
	{
		// a method named Any outside the framework may mean anything
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => OwnPredicates.Any(p.Tags, t => t == "iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*OwnPredicates.Any*");
	}

	[Test]
	public void Where_AnyOverACollectionOfObjects_ThrowsNotSupported()
	{
		// ES|QL has the column lines.sku and none for lines itself
		var query = CreateQuery<LinedProduct>()
			.From("orders")
			.Where(o => o.Lines.Any());

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*collection of objects*");
	}

	[Test]
	public void Where_ContainsOverAListOfUris_TranslatesToMatch()
	{
		// a Uri is a class, but the serializer writes it as a string, which MATCH compares
		var esql = CreateQuery<LinkedProduct>()
			.From("products")
			.Where(p => p.Links.Contains(new Uri("https://www.elastic.co/")))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (links IS NOT NULL AND MATCH(links, "https://www.elastic.co/"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsOverACollectionOfObjects_ThrowsNotSupported()
	{
		var line = new ProductLine { Sku = "a" };

		var query = CreateQuery<LinedProduct>()
			.From("orders")
			.Where(o => o.Lines.Contains(line));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*collection of objects*");
	}

	[Test]
	public void Where_AnyOverAListOfDictionaries_ThrowsNotSupported()
	{
		// a dictionary is one object in the mapping, as a class is
		var query = CreateQuery<LabeledProduct>()
			.From("products")
			.Where(p => p.Labels.Any());

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*collection of objects*");
	}

	[Test]
	public void Where_AnyOverAListWithoutASerializerContract_ThrowsNotSupported()
	{
		// the serializer never writes the ignored lines and has no contract for their type, so
		// the element is judged by its shape, a class
		var query = CreateQuery<ArchivedLinesProduct>()
			.From("orders")
			.Where(o => o.Lines.Any());

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*collection of objects*");
	}

	[Test]
	public void Where_AnyOverADictionary_IsNotTranslated()
	{
		// a dictionary is one object in the mapping, not a field holding values
		var query = CreateQuery<AttributedProduct>()
			.From("products")
			.Where(p => p.Attributes.Any());

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Enumerable.Any*");
	}

	[Test]
	public void Where_AnyWithANullGuardAfterThePredicate_DropsTheGuard()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot" && t != null))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AllWithANullGuardThatAdmitsNull_DropsTheGuard()
	{
		// "x == null || P(x)" is P(x) over values that are never null
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.All(t => t == null || t == "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (tags IS NULL OR (MV_COUNT(MV_DEDUPE(tags)) == 1 AND MATCH(tags, "iot")))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AControlCharacterInTheValue_IsEscaped()
	{
		// the value is written the way a single field's is, escapes included
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "a\nb\t\"c"))
			.ToString();

		_ = esql.Should().Contain(@"MATCH(tags, ""a\nb\t\""c"")");
	}

	[Test]
	public void Where_AnyOverACollectionWithARegisteredConverter_ThrowsNotSupported()
	{
		// a converter among the serializer's converters writes every List<string> field, and
		// the value compared would not go through it
		var provider = new EsqlQueryProvider(new JsonSerializerOptions
		{
			TypeInfoResolver = EsqlTestMappingContext.Default,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			Converters = { new PrefixedTagsConverter() }
		});

		var query = new EsqlQueryable<TaggedProduct>(provider)
			.From("products")
			.Where(p => p.Categories.Any(c => c == "pumps"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*JsonConverter*");
	}

	[Test]
	public void Where_ContainsOverACollectionTypeWithAConverter_ThrowsNotSupported()
	{
		// the converter named on the collection type writes the field as well
		var query = CreateQuery<TypeConvertedTagsProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*JsonConverter*");
	}

	[Test]
	public void Where_NegatedContains_TranslatesToNotMatch()
	{
		// a document without the field, or on a shard whose index does not map it, answers
		// the IS NOT NULL false, so the negation keeps it, as !Contains does over an empty
		// sequence
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => !p.Tags.Contains("iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT (tags IS NOT NULL AND MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsAfterTake_ThrowsNotSupported()
	{
		// Elasticsearch rejects MATCH after LIMIT, so the query is refused when it is translated
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Take(10)
			.Where(p => p.Tags.Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*after LIMIT*");
	}

	[Test]
	public void Where_ContainsAfterGroupBy_ThrowsNotSupported()
	{
		// the Where lands behind STATS, where MATCH is rejected as it is after LIMIT
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, Ips = EsqlFunctions.Values(g, l => l.ClientIp) })
			.Where(r => r.Ips.Contains("10.0.0.1"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*after STATS*");
	}

	[Test]
	public void Where_ContainsAfterFork_ThrowsNotSupported()
	{
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Fork(b => b.Take(10), b => b.Take(20))
			.Where(p => p.Tags.Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*after FORK*");
	}

	[Test]
	public void Where_AnyWithAnOrderingAfterTake_TranslatesToMvMax()
	{
		// MV_MAX is evaluated per row, so a LIMIT before it is no obstacle
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Take(10)
			.Where(p => p.Ratings.Any(r => r > 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | LIMIT 10
            | WHERE (ratings IS NOT NULL AND MV_MAX(ratings) > 3)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_ContainsAfterARawLimit_ThrowsNotSupported()
	{
		// a raw fragment is text, and a LIMIT written there is in the way of MATCH as well
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.RawEsql("limit 10")
			.Where(p => p.Tags.Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*after LIMIT*");
	}

	[Test]
	public void Where_ContainsAfterARawStats_ThrowsNotSupported()
	{
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.RawEsql("STATS n = COUNT(*) BY tags")
			.Where(p => p.Tags.Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*after STATS*");
	}

	[Test]
	public void Where_ContainsInAForkBranchAfterTake_ThrowsNotSupported()
	{
		// Elasticsearch verifies a branch on top of the pipeline before the Fork, so the LIMIT
		// there is in the way of MATCH in the branch as well
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Take(10)
			.Fork(b => b.Where(p => p.Tags.Contains("iot")), b => b.Take(5));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*after LIMIT*");
	}

	[Test]
	public void Where_ContainsInsideAForkBranch_TranslatesToMatch()
	{
		// a branch is a pipeline of its own, so the FORK it belongs to does not precede its WHERE
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Fork(b => b.Where(p => p.Tags.Contains("iot")), b => b.Take(10))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | FORK (WHERE (tags IS NOT NULL AND MATCH(tags, "iot"))) (LIMIT 10)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithAMethodOtherThanContains_ThrowsNotSupported()
	{
		// Remove takes one value and returns a bool, as Contains does, but it tests no membership
		var seen = new List<string> { "iot" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => seen.Remove(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Enumerable.Any*");
	}

	[Test]
	public void Where_AnyWithEqualityOnAnEnumList_TranslatesToMatch()
	{
		// C# compares an enum as its number: "x == Priority.High" is "(int)x == 2"
		var esql = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Priorities.Any(x => x == Priority.High))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (priorities IS NOT NULL AND MATCH(priorities, 2))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithEqualityOnAnEnumWrittenByName_MatchesTheName()
	{
		// the number the compiler leaves in the tree is turned back into the enum, which its
		// converter writes by name, as the field holds it
		var esql = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Grades.Any(g => g == Grade.High))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (grades IS NOT NULL AND MATCH(grades, "High"))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithACapturedEnum_ParameterizesItAsTheSerializerWritesIt()
	{
		// the captured enum keeps its name as a parameter, and its value is written by name
		var grade = Grade.High;

		var query = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Grades.Any(g => g == grade));

		var esql = query.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (grades IS NOT NULL AND MATCH(grades, ?grade))
            """.NativeLineEndings());
		_ = query.GetParameters()!.Parameters["grade"].GetString().Should().Be("High");
	}

	[Test]
	public void Where_AnyWithACapturedNumberCastToAnEnum_ParameterizesTheEnum()
	{
		// the captured number keeps its name as a parameter, which holds the enum it is cast to
		var level = 1;

		var query = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Grades.Any(g => g == (Grade)level));

		var esql = query.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (grades IS NOT NULL AND MATCH(grades, ?level))
            """.NativeLineEndings());
		_ = query.GetParameters()!.Parameters["level"].GetString().Should().Be("High");
	}

	[Test]
	public void Where_AnyWithACapturedNumberCastToAnEnum_ReadsTheValueOnce()
	{
		var holder = new CountingNumber(1);

		_ = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Grades.Any(g => g == (Grade)holder.Value))
			.ToString();

		_ = holder.Reads.Should().Be(1);
	}

	[Test]
	public void Where_AnyWithAComputedEnumValue_TranslatesItAsAScalarComparisonDoes()
	{
		// a number read only when the query runs is rendered as it is, as a scalar comparison
		// renders it
		var esql = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Priorities.Any(x => x == (Priority)Math.Abs(-2)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (priorities IS NOT NULL AND MATCH(priorities, ABS(-2)))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithAnOrderingOnNarrowIntegers_TranslatesToMvMax()
	{
		// a short and a byte are compared as int: "s > 3" is "(int)s > 3"
		var esql = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Sizes.Any(s => s > 3) && p.Scores.Any(s => s > 3))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE ((sizes IS NOT NULL AND MV_MAX(sizes) > 3) AND (scores IS NOT NULL AND MV_MAX(scores) > 3))
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithAFractionalBoundOnAnIntegerList_TranslatesToMvMax()
	{
		// "r > 3.5" is "(double)r > 3.5"
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.Any(r => r > 3.5))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (ratings IS NOT NULL AND MV_MAX(ratings) > 3.5)
            """.NativeLineEndings());
	}

	[Test]
	public void Where_AnyWithARelativeDate_TranslatesItAsAScalarComparisonDoes()
	{
		var esql = CreateQuery<TypedValuesProduct>()
			.From("products")
			.Where(p => p.Restocks.Any(d => d > DateTime.UtcNow.AddDays(-7)))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (restocks IS NOT NULL AND MV_MAX(restocks) > (NOW() - 7 days))
            """.NativeLineEndings());
	}

	/// <summary>
	/// Translates the predicate of a Where over an in-memory source, the way the query
	/// syntax leaves it behind transparent identifiers.
	/// </summary>
	private static string Translate<TSource>(IQueryable<TSource> query)
	{
		var whereCall = (MethodCallExpression)query.Expression;
		var predicate = (LambdaExpression)((UnaryExpression)whereCall.Arguments[1]).Operand;

		var context = new EsqlTranslationContext
		{
			Metadata = QueryProvider.Metadata,
			InlineParameters = true
		};

		return new WhereClauseVisitor(context).Translate(predicate.Body);
	}

	private static class OwnPredicates
	{
		public static bool Any<T>(IEnumerable<T> source, Func<T, bool> predicate) => source.Any(predicate);

		public static bool SameTag(string value, IEqualityComparer<string> comparer) => comparer.Equals(value, "iot");
	}

	private sealed class CountingNumber(int value)
	{
		public int Reads { get; private set; }

		public int Value
		{
			get
			{
				Reads++;
				return value;
			}
		}
	}
}
