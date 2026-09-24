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
/// whole: MATCH for a value, MV_MIN and MV_MAX for an ordering, MV_COUNT for Any(), with
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
	public void Where_AnyWithoutPredicate_TranslatesToMvCount()
	{
		// the count is coalesced: a missing field is an empty sequence, where Any() is false
		// and its negation true
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any())
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE COALESCE(MV_COUNT(tags), 0) > 0
            """.NativeLineEndings());
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

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Enumerable.Any*");
	}

	[Test]
	public void Where_ContainsNullOnAField_ThrowsNotSupported()
	{
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains(null));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Contains*");
	}

	[Test]
	public void Where_AnyBehindTransparentIdentifiers_StripsAllPrefixes()
	{
		var source = new[] { new { Outer = new { Outer = new TaggedProduct() } } }.AsQueryable();

		_ = Translate(source.Where(x => x.Outer.Outer.Tags.Any())).Should().Be("COALESCE(MV_COUNT(tags), 0) > 0");
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
	public void Where_AnyOverACapturedReadOnlyCollection_ThrowsNotSupported()
	{
		// a wrapper hands Contains to the list it wraps, which may compare in any way
		var wanted = new ReadOnlyCollection<string>(["iot"]);

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*ReadOnlyCollection*way of its own*");
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

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Enumerable.Any*");
	}

	[Test]
	public void Where_AllOverACapturedArray_ThrowsNotSupported()
	{
		// "every value is one of these" holds for one value at a time
		var wanted = new[] { "iot", "water" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.All(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*individual values*");
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
}
