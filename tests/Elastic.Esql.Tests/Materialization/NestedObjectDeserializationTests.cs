// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class NestedObjectDeserializationTests
{
	// =========================================================================
	// Basic Nesting
	// =========================================================================

	[Test]
	public void ReadRows_SingleLevelNesting_DeserializesNestedObject()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "123 Main St", "Springfield"]
			  ]
			}
			""";

		var results = ReadRows<PersonModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("John");
		results[0].Address.Should().NotBeNull();
		results[0].Address!.Street.Should().Be("123 Main St");
		results[0].Address!.City.Should().Be("Springfield");
	}

	[Test]
	public void ReadRows_DeepNesting_ThreeLevels_Deserializes()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "middle.leaf.value", "type": "keyword" }
			  ],
			  "values": [
			    ["root", "deep-value"]
			  ]
			}
			""";

		var results = ReadRows<DeepRoot>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("root");
		results[0].Middle.Should().NotBeNull();
		results[0].Middle!.Leaf.Should().NotBeNull();
		results[0].Middle!.Leaf!.Value.Should().Be("deep-value");
	}

	[Test]
	public void ReadRows_DeepNesting_WithSiblingAtMiddleLevel()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "middle.label", "type": "keyword" },
			    { "name": "middle.leaf.value", "type": "keyword" }
			  ],
			  "values": [
			    ["root", "mid-label", "deep-value"]
			  ]
			}
			""";

		var results = ReadRows<DeepRoot>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("root");
		results[0].Middle.Should().NotBeNull();
		results[0].Middle!.Label.Should().Be("mid-label");
		results[0].Middle!.Leaf.Should().NotBeNull();
		results[0].Middle!.Leaf!.Value.Should().Be("deep-value");
	}

	[Test]
	public void ReadRows_FourLevelNesting_Deserializes()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "nested.child.inner.data", "type": "keyword" }
			  ],
			  "values": [
			    ["root", "level-4-data"]
			  ]
			}
			""";

		var results = ReadRows<Level1Root>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("root");
		results[0].Nested.Should().NotBeNull();
		results[0].Nested!.Child.Should().NotBeNull();
		results[0].Nested!.Child!.Inner.Should().NotBeNull();
		results[0].Nested!.Child!.Inner!.Data.Should().Be("level-4-data");
	}

	[Test]
	public void ReadRows_FourLevelNesting_MixedDepthColumns()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "nested.info", "type": "keyword" },
			    { "name": "nested.child.tag", "type": "keyword" },
			    { "name": "nested.child.inner.data", "type": "keyword" }
			  ],
			  "values": [
			    ["root", "info-val", "tag-val", "deep-data"]
			  ]
			}
			""";

		var results = ReadRows<Level1Root>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("root");
		results[0].Nested!.Info.Should().Be("info-val");
		results[0].Nested!.Child!.Tag.Should().Be("tag-val");
		results[0].Nested!.Child!.Inner!.Data.Should().Be("deep-data");
	}

	[Test]
	public void ReadRows_FourLevelNesting_NonContiguousColumns()
	{
		var json = """
			{
			  "columns": [
			    { "name": "nested.child.inner.data", "type": "keyword" },
			    { "name": "name", "type": "keyword" },
			    { "name": "nested.info", "type": "keyword" },
			    { "name": "nested.child.tag", "type": "keyword" }
			  ],
			  "values": [
			    ["deep-data", "root", "info-val", "tag-val"]
			  ]
			}
			""";

		var results = ReadRows<Level1Root>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("root");
		results[0].Nested!.Info.Should().Be("info-val");
		results[0].Nested!.Child!.Tag.Should().Be("tag-val");
		results[0].Nested!.Child!.Inner!.Data.Should().Be("deep-data");
	}

	[Test]
	public void ReadRows_FourLevelNesting_PartialNulls_IntermediateCreated()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "nested.info", "type": "keyword" },
			    { "name": "nested.child.tag", "type": "keyword" },
			    { "name": "nested.child.inner.data", "type": "keyword" }
			  ],
			  "values": [
			    ["root", "info-val", null, "deep-data"]
			  ]
			}
			""";

		var results = ReadRows<Level1Root>(json);

		results.Should().HaveCount(1);
		results[0].Nested!.Info.Should().Be("info-val");
		results[0].Nested!.Child!.Tag.Should().Be(string.Empty);
		results[0].Nested!.Child!.Inner!.Data.Should().Be("deep-data");
	}

	[Test]
	public void ReadRows_FourLevelNesting_DeepestNull_IntermediatesNull()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "nested.info", "type": "keyword" },
			    { "name": "nested.child.tag", "type": "keyword" },
			    { "name": "nested.child.inner.data", "type": "keyword" }
			  ],
			  "values": [
			    ["root", "info-val", null, null]
			  ]
			}
			""";

		var results = ReadRows<Level1Root>(json);

		results.Should().HaveCount(1);
		results[0].Nested!.Info.Should().Be("info-val");
		results[0].Nested!.Child.Should().BeNull();
	}

	[Test]
	public void ReadRows_FourLevelNesting_MultipleRows_MixedDepths()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "nested.info", "type": "keyword" },
			    { "name": "nested.child.tag", "type": "keyword" },
			    { "name": "nested.child.inner.data", "type": "keyword" }
			  ],
			  "values": [
			    ["row1", "info1", "tag1", "data1"],
			    ["row2", null, null, null],
			    ["row3", "info3", null, null],
			    ["row4", "info4", "tag4", null]
			  ]
			}
			""";

		var results = ReadRows<Level1Root>(json);

		results.Should().HaveCount(4);

		results[0].Nested!.Info.Should().Be("info1");
		results[0].Nested!.Child!.Tag.Should().Be("tag1");
		results[0].Nested!.Child!.Inner!.Data.Should().Be("data1");

		results[1].Nested.Should().BeNull();

		results[2].Nested!.Info.Should().Be("info3");
		results[2].Nested!.Child.Should().BeNull();

		results[3].Nested!.Info.Should().Be("info4");
		results[3].Nested!.Child!.Tag.Should().Be("tag4");
		results[3].Nested!.Child!.Inner.Should().BeNull();
	}

	[Test]
	public void ReadRows_MultipleSiblingNestedObjects_Deserializes()
	{
		var json = """
			{
			  "columns": [
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" },
			    { "name": "contact.email", "type": "keyword" },
			    { "name": "contact.phone", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["123 Main St", "Springfield", "john@test.com", "555-1234", "John"]
			  ]
			}
			""";

		var results = ReadRows<MultiNestedModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("John");
		results[0].Address.Should().NotBeNull();
		results[0].Address!.Street.Should().Be("123 Main St");
		results[0].Address!.City.Should().Be("Springfield");
		results[0].Contact.Should().NotBeNull();
		results[0].Contact!.Email.Should().Be("john@test.com");
		results[0].Contact!.Phone.Should().Be("555-1234");
	}

	[Test]
	public void ReadRows_SingleNestedProperty_OnlyOneSubField()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "123 Main St"]
			  ]
			}
			""";

		var results = ReadRows<PersonModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("John");
		results[0].Address.Should().NotBeNull();
		results[0].Address!.Street.Should().Be("123 Main St");
		results[0].Address!.City.Should().Be(string.Empty);
	}

	// =========================================================================
	// Non-Contiguous Nested Columns
	// =========================================================================

	[Test]
	public void ReadRows_NonContiguousNestedColumns_StillGroups()
	{
		var json = """
			{
			  "columns": [
			    { "name": "address.street", "type": "keyword" },
			    { "name": "name", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    ["123 Main St", "John", "Springfield"]
			  ]
			}
			""";

		var results = ReadRows<PersonModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("John");
		results[0].Address.Should().NotBeNull();
		results[0].Address!.Street.Should().Be("123 Main St");
		results[0].Address!.City.Should().Be("Springfield");
	}

	// =========================================================================
	// JsonPropertyName Precedence
	// =========================================================================

	[Test]
	public void ReadRows_JsonPropertyNameWithDots_TakesPrecedenceOverNesting()
	{
		var json = """
			{
			  "columns": [
			    { "name": "address.street", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["123 Main St", "John"]
			  ]
			}
			""";

		var results = ReadRows<DotNamePrecedenceModel>(json);

		results.Should().HaveCount(1);
		results[0].AddressStreet.Should().Be("123 Main St");
		results[0].Name.Should().Be("John");
	}

	[Test]
	public void ReadRows_MixedDotNameAndNestedObject_BothWork()
	{
		var json = """
			{
			  "columns": [
			    { "name": "address.full", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    ["123 Main St, Springfield", "123 Main St", "Springfield"]
			  ]
			}
			""";

		var results = ReadRows<MixedDotModel>(json);

		results.Should().HaveCount(1);
		results[0].AddressFull.Should().Be("123 Main St, Springfield");
		results[0].Address.Should().NotBeNull();
		results[0].Address!.Street.Should().Be("123 Main St");
		results[0].Address!.City.Should().Be("Springfield");
	}

	[Test]
	public void ReadRows_SubTypeWithJsonPropertyNameDots_ResolvesCorrectly()
	{
		var json = """
			{
			  "columns": [
			    { "name": "inner.x.y", "type": "keyword" },
			    { "name": "inner.z", "type": "keyword" }
			  ],
			  "values": [
			    ["dotted-value", "plain-value"]
			  ]
			}
			""";

		var results = ReadRows<OuterWithDotInner>(json);

		results.Should().HaveCount(1);
		results[0].Inner.Should().NotBeNull();
		results[0].Inner!.Xy.Should().Be("dotted-value");
		results[0].Inner!.Z.Should().Be("plain-value");
	}

	[Test]
	public void ReadRows_FlatDotFallback_ColumnWithDotsNoMatchingNestedType()
	{
		var json = """
			{
			  "columns": [
			    { "name": "unknown.prop", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["fallback-value", "John"]
			  ]
			}
			""";

		var results = ReadRows<FlatDotFallbackModel>(json);

		results.Should().HaveCount(1);
		results[0].UnknownProp.Should().Be("fallback-value");
		results[0].Name.Should().Be("John");
	}

	// =========================================================================
	// Null Handling
	// =========================================================================

	[Test]
	public void ReadRows_AllSubPropertiesNull_NestedObjectStaysNull()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    ["John", null, null]
			  ]
			}
			""";

		var results = ReadRows<NullableNestedModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("John");
		results[0].Address.Should().BeNull();
	}

	[Test]
	public void ReadRows_SomeSubPropertiesNull_PartialNestedObject()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "123 Main St", null]
			  ]
			}
			""";

		var results = ReadRows<PersonModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("John");
		results[0].Address.Should().NotBeNull();
		results[0].Address!.Street.Should().Be("123 Main St");
		results[0].Address!.City.Should().Be(string.Empty);
	}

	[Test]
	public void ReadRows_AllColumnsNull_IncludingNested_AllDefaults()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    [null, null, null]
			  ]
			}
			""";

		var results = ReadRows<NullableNestedModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be(string.Empty);
		results[0].Address.Should().BeNull();
	}

	[Test]
	public void ReadRows_DeepNesting_AllNull_TopLevelNull()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "middle.leaf.value", "type": "keyword" }
			  ],
			  "values": [
			    ["root", null]
			  ]
			}
			""";

		var results = ReadRows<DeepRoot>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("root");
		results[0].Middle.Should().BeNull();
	}

	// =========================================================================
	// Collection Columns in Nested Objects
	// =========================================================================

	[Test]
	public void ReadRows_NestedCollectionProperty_ScalarWrappedInArray()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" },
			    { "name": "address.tags", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "Springfield", "solo-tag"]
			  ]
			}
			""";

		var results = ReadRows<PersonWithTaggedAddress>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("John");
		results[0].Address.Should().NotBeNull();
		results[0].Address!.City.Should().Be("Springfield");
		results[0].Address!.Tags.Should().BeEquivalentTo(["solo-tag"]);
	}

	[Test]
	public void ReadRows_NestedCollectionProperty_ArrayPassesThrough()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" },
			    { "name": "address.tags", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "Springfield", ["tag-a", "tag-b"]]
			  ]
			}
			""";

		var results = ReadRows<PersonWithTaggedAddress>(json);

		results.Should().HaveCount(1);
		results[0].Address.Should().NotBeNull();
		results[0].Address!.Tags.Should().BeEquivalentTo(["tag-a", "tag-b"]);
	}

	[Test]
	public void ReadRows_NestedCollectionProperty_NullValue_StaysDefault()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" },
			    { "name": "address.tags", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "Springfield", null]
			  ]
			}
			""";

		var results = ReadRows<PersonWithTaggedAddress>(json);

		results.Should().HaveCount(1);
		results[0].Address.Should().NotBeNull();
		results[0].Address!.City.Should().Be("Springfield");
		results[0].Address!.Tags.Should().BeEmpty();
	}

	// =========================================================================
	// Multiple Rows
	// =========================================================================

	[Test]
	public void ReadRows_MultipleRows_EachDeserializedCorrectly()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    ["Alice", "1st Ave", "New York"],
			    ["Bob", "2nd Ave", "Boston"],
			    ["Charlie", "3rd Ave", "Chicago"]
			  ]
			}
			""";

		var results = ReadRows<PersonModel>(json);

		results.Should().HaveCount(3);

		results[0].Name.Should().Be("Alice");
		results[0].Address!.Street.Should().Be("1st Ave");
		results[0].Address!.City.Should().Be("New York");

		results[1].Name.Should().Be("Bob");
		results[1].Address!.Street.Should().Be("2nd Ave");
		results[1].Address!.City.Should().Be("Boston");

		results[2].Name.Should().Be("Charlie");
		results[2].Address!.Street.Should().Be("3rd Ave");
		results[2].Address!.City.Should().Be("Chicago");
	}

	[Test]
	public void ReadRows_MultipleRows_MixedNullAndNonNullNested()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    ["Alice", "1st Ave", "New York"],
			    ["Bob", null, null],
			    ["Charlie", "3rd Ave", null]
			  ]
			}
			""";

		var results = ReadRows<NullableNestedModel>(json);

		results.Should().HaveCount(3);

		results[0].Address.Should().NotBeNull();
		results[0].Address!.Street.Should().Be("1st Ave");

		results[1].Address.Should().BeNull();

		results[2].Address.Should().NotBeNull();
		results[2].Address!.Street.Should().Be("3rd Ave");
		results[2].Address!.City.Should().Be(string.Empty);
	}

	// =========================================================================
	// Empty Values
	// =========================================================================

	[Test]
	public void ReadRows_EmptyValues_WithNestedColumns_ReturnsEmpty()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" }
			  ],
			  "values": []
			}
			""";

		var results = ReadRows<PersonModel>(json);

		results.Should().BeEmpty();
	}

	// =========================================================================
	// Max Depth Guard
	// =========================================================================

	[Test]
	public void ReadRows_NestingDepthExceedsMaxDepth_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "middle.leaf.value", "type": "keyword" }
			  ],
			  "values": [
			    ["root", "deep"]
			  ]
			}
			""";

		var act = () => ReadRowsWithMaxDepth<DeepRoot>(json, maxDepth: 2);

		_ = act.Should()
			.Throw<JsonException>()
			.WithMessage("*nesting depth*exceeds*");
	}

	[Test]
	public void ReadRows_NestingDepthWithinMaxDepth_Succeeds()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "middle.leaf.value", "type": "keyword" }
			  ],
			  "values": [
			    ["root", "deep"]
			  ]
			}
			""";

		var results = ReadRowsWithMaxDepth<DeepRoot>(json, maxDepth: 3);

		results.Should().HaveCount(1);
		results[0].Middle!.Leaf!.Value.Should().Be("deep");
	}

	// =========================================================================
	// Row Count Validation with Nested Columns
	// =========================================================================

	[Test]
	public void ReadRows_NestedColumns_MoreValuesThanColumns_Throws()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "123 Main", "extra"]
			  ]
			}
			""";

		var act = () => ReadRows<PersonModel>(json);

		_ = act.Should()
			.Throw<JsonException>()
			.WithMessage("*more values*");
	}

	[Test]
	public void ReadRows_NestedColumns_FewerValuesThanColumns_Throws()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "address.street", "type": "keyword" },
			    { "name": "address.city", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "123 Main"]
			  ]
			}
			""";

		var act = () => ReadRows<PersonModel>(json);

		_ = act.Should()
			.Throw<JsonException>()
			.WithMessage("*fewer values*");
	}

	// =========================================================================
	// Flat Fast Path Regression
	// =========================================================================

	[Test]
	public void ReadRows_FlatColumns_NoDots_StillWorks()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["hello", 42]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be("hello");
		results[0].Count.Should().Be(42);
	}

	[Test]
	public void ReadRows_FlatColumnsWithCollectionCoercion_StillWorks()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "tags", "type": "keyword" }
			  ],
			  "values": [
			    ["item1", "solo-tag"]
			  ]
			}
			""";

		var results = ReadRows<ArrayStringPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Tags.Should().BeEquivalentTo(["solo-tag"]);
	}

	// =========================================================================
	// Nested Objects with Numeric and Boolean Values
	// =========================================================================

	[Test]
	public void ReadRows_NestedObjectWithMixedTypes_Deserializes()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "contact.email", "type": "keyword" },
			    { "name": "contact.phone", "type": "keyword" }
			  ],
			  "values": [
			    ["John", "john@example.com", "555-0100"]
			  ]
			}
			""";

		var results = ReadRows<MultiNestedModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("John");
		results[0].Contact!.Email.Should().Be("john@example.com");
		results[0].Contact!.Phone.Should().Be("555-0100");
		results[0].Address.Should().BeNull();
	}

	// =========================================================================
	// Helpers
	// =========================================================================

	private static List<T> ReadRows<T>(string json)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default
			)
		};
		var metadata = new JsonMetadataManager(options);
		var reader = new EsqlResponseReader(metadata);
		return reader.ReadRows<T>(stream).Rows.ToList();
	}

	private static List<T> ReadRowsWithMaxDepth<T>(string json, int maxDepth)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			MaxDepth = maxDepth,
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default
			)
		};
		var metadata = new JsonMetadataManager(options);
		var reader = new EsqlResponseReader(metadata);
		return reader.ReadRows<T>(stream).Rows.ToList();
	}
}
