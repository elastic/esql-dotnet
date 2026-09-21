// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>
/// Predicates over multi-value document fields. A document matches when any of the
/// field's values matches, which is what MATCH does, and unlike MV_EXPAND it does not
/// duplicate rows.
/// </summary>
public class MultiValueFieldTests : EsqlTestBase
{
	[Test]
	public void Any_WithEqualityPredicate_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "water"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE MATCH(tags, "water")
            """.NativeLineEndings());
	}

	[Test]
	public void Any_WithReversedEqualityPredicate_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => "water" == t))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE MATCH(tags, "water")
            """.NativeLineEndings());
	}

	[Test]
	public void Contains_OnAFieldCollection_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("water"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE MATCH(tags, "water")
            """.NativeLineEndings());
	}

	[Test]
	public void Any_WithoutPredicate_TranslatesToMvCount()
	{
		// the count is coalesced: a missing field is an empty sequence, where Any() is
		// false, and so is its negation's opposite
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
	public void Any_OnAListProperty_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Categories.Any(c => c == "pumps"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE MATCH(categories, "pumps")
            """.NativeLineEndings());
	}

	[Test]
	public void Any_OnANumericList_TranslatesToMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Ratings.Any(r => r == 42))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE MATCH(ratings, 42)
            """.NativeLineEndings());
	}

	[Test]
	public void TwoAnyPredicates_CombineWithOr()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "water") || p.Tags.Any(t => t == "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (MATCH(tags, "water") OR MATCH(tags, "iot"))
            """.NativeLineEndings());
	}

	[Test]
	public void TwoAnyPredicates_CombineWithAnd()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot") && p.Tags.Any(t => t == "industrial"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (MATCH(tags, "iot") AND MATCH(tags, "industrial"))
            """.NativeLineEndings());
	}

	[Test]
	public void NegatedAny_TranslatesToNotMatch()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => !p.Tags.Any(t => t == "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT MATCH(tags, "iot")
            """.NativeLineEndings());
	}

	[Test]
	public void Any_CombinedWithAScalarPredicate()
	{
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "water") && p.Name == "pump")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE (MATCH(tags, "water") AND name == "pump")
            """.NativeLineEndings());
	}

	[Test]
	public void Any_WithACapturedValue()
	{
		var tag = "water";

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == tag))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE MATCH(tags, "water")
            """.NativeLineEndings());
	}

	[Test]
	public void ContainsOverAConstantCollection_StillTranslatesToIn()
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
	public void All_WithEqualityPredicate_RequiresASingleMatchingValue()
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
	public void All_OnANumericList_CountsDistinctValues()
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
	public void All_WithInequalityPredicate_TranslatesToNotMatch()
	{
		// "every value differs from x" is "none of the values is x"
		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.All(t => t != "iot"))
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM products
            | WHERE NOT MATCH(tags, "iot")
            """.NativeLineEndings());
	}

	[Test]
	public void All_CombinedWithAScalarPredicate()
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
}
