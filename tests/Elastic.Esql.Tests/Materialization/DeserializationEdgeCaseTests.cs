// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class DeserializationEdgeCaseTests
{
	// =========================================================================
	// Scalar-to-Array Coercion
	// =========================================================================

	[Test]
	public void ReadRows_StringArrayProperty_ScalarStringValue_WrapsInArray()
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
		results[0].Name.Should().Be("item1");
		results[0].Tags.Should().BeEquivalentTo(["solo-tag"]);
	}

	[Test]
	public void ReadRows_ListIntProperty_ScalarIntValue_WrapsInArray()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "values", "type": "integer" }
			  ],
			  "values": [
			    ["row1", 42]
			  ]
			}
			""";

		var results = ReadRows<ListIntPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("row1");
		results[0].Values.Should().BeEquivalentTo([42]);
	}

	[Test]
	public void ReadRows_ListStringProperty_NullValue_PropertyStaysDefault()
	{
		var json = """
			{
			  "columns": [
			    { "name": "items", "type": "keyword" }
			  ],
			  "values": [
			    [null]
			  ]
			}
			""";

		var results = ReadRows<ListStringPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Items.Should().BeEmpty();
	}

	[Test]
	public void ReadRows_StringArrayProperty_ArrayValue_NoDoubleWrapping()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "tags", "type": "keyword" }
			  ],
			  "values": [
			    ["item1", ["tag-a", "tag-b"]]
			  ]
			}
			""";

		var results = ReadRows<ArrayStringPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Tags.Should().BeEquivalentTo(["tag-a", "tag-b"]);
	}

	[Test]
	public void ReadRows_ListIntProperty_ArrayValue_PassesThrough()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "values", "type": "integer" }
			  ],
			  "values": [
			    ["row1", [10, 20, 30]]
			  ]
			}
			""";

		var results = ReadRows<ListIntPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Values.Should().BeEquivalentTo([10, 20, 30]);
	}

	[Test]
	public void ReadRows_ListStringProperty_EmptyArray_ReturnsEmptyCollection()
	{
		var json = """
			{
			  "columns": [
			    { "name": "items", "type": "keyword" }
			  ],
			  "values": [
			    [[]]
			  ]
			}
			""";

		var results = ReadRows<ListStringPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Items.Should().BeEmpty();
	}

	// =========================================================================
	// Array-to-Scalar Mismatch
	// =========================================================================

	[Test]
	public void ReadRows_ScalarStringProperty_ReceivesArray_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    [["a", "b"], 1]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarStringModel>(json);

		_ = act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_ScalarIntProperty_ReceivesArray_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    [[1, 2], "row1"]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarIntModel>(json);

		_ = act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_ScalarDoubleProperty_ReceivesSingleElementArray_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "double" }
			  ],
			  "values": [
			    [[1.5]]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarDoubleModel>(json);

		_ = act.Should().Throw<JsonException>();
	}

	// =========================================================================
	// Null Handling Edge Cases
	// =========================================================================

	[Test]
	public void ReadRows_NonNullableValueTypes_ReceiveNull_GetDefaults()
	{
		var json = """
			{
			  "columns": [
			    { "name": "count", "type": "integer" },
			    { "name": "active", "type": "boolean" },
			    { "name": "score", "type": "double" }
			  ],
			  "values": [
			    [null, null, null]
			  ]
			}
			""";

		var results = ReadRows<NonNullableValueModel>(json);

		results.Should().HaveCount(1);
		results[0].Count.Should().Be(0);
		results[0].Active.Should().BeFalse();
		results[0].Score.Should().Be(0.0);
	}

	[Test]
	public void ReadRows_NullableInt_ReceivesNull_IsNull()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    [null, "test"]
			  ]
			}
			""";

		var results = ReadRows<NullableIntModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().BeNull();
		results[0].Name.Should().Be("test");
	}

	[Test]
	public void ReadRows_NullableInt_ReceivesValue_HasValue()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    [99, "test"]
			  ]
			}
			""";

		var results = ReadRows<NullableIntModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be(99);
	}

	[Test]
	public void ReadRows_AllColumnsNull_ObjectWithDefaults()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "count", "type": "integer" },
			    { "name": "score", "type": "double" }
			  ],
			  "values": [
			    [null, null, null]
			  ]
			}
			""";

		var results = ReadRows<AllNullableModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().BeNull();
		results[0].Count.Should().BeNull();
		results[0].Score.Should().BeNull();
	}

	[Test]
	public void ReadRows_NonNullableString_ReceivesNull_StaysAtInitializer()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    [null, 5]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be(string.Empty);
		results[0].Count.Should().Be(5);
	}

	[Test]
	public void ReadRows_ListProperty_NullValue_StaysAtDefaultEmptyList()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "values", "type": "integer" }
			  ],
			  "values": [
			    ["row1", null]
			  ]
			}
			""";

		var results = ReadRows<ListIntPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Name.Should().Be("row1");
		results[0].Values.Should().BeEmpty();
	}

	// =========================================================================
	// Type Mismatches (JSON type vs .NET type)
	// =========================================================================

	[Test]
	public void ReadRows_StringValueForIntProperty_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["hello", "row1"]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarIntModel>(json);

		_ = act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_NumberValueForStringProperty_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    [42, 1]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarStringModel>(json);

		_ = act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_BooleanValueForIntProperty_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "boolean" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    [true, "row1"]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarIntModel>(json);

		_ = act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_NumberValueForBoolProperty_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "active", "type": "integer" }
			  ],
			  "values": [
			    [42]
			  ]
			}
			""";

		var act = () => ReadRows<BoolOnlyModel>(json);

		_ = act.Should().Throw<JsonException>();
	}

	// =========================================================================
	// Primitive Scalar Deserialization (single-column)
	// =========================================================================

	[Test]
	public void ReadRows_SingleIntColumn_ReturnsInt()
	{
		var json = """
			{
			  "columns": [{ "name": "count", "type": "integer" }],
			  "values": [[42]]
			}
			""";

		var results = ReadRows<int>(json);

		results.Should().Equal([42]);
	}

	[Test]
	public void ReadRows_SingleStringColumn_ReturnsString()
	{
		var json = """
			{
			  "columns": [{ "name": "name", "type": "keyword" }],
			  "values": [["hello"]]
			}
			""";

		var results = ReadRows<string>(json);

		results.Should().Equal(["hello"]);
	}

	[Test]
	public void ReadRows_SingleLongColumn_ReturnsLong()
	{
		var json = """
			{
			  "columns": [{ "name": "value", "type": "long" }],
			  "values": [[9999999999]]
			}
			""";

		var results = ReadRows<long>(json);

		results.Should().Equal([9999999999L]);
	}

	[Test]
	public void ReadRows_SingleBoolColumn_ReturnsBool()
	{
		var json = """
			{
			  "columns": [{ "name": "flag", "type": "boolean" }],
			  "values": [[true], [false]]
			}
			""";

		var results = ReadRows<bool>(json);

		results.Should().Equal([true, false]);
	}

	[Test]
	public void ReadRows_SingleDoubleColumn_ReturnsDouble()
	{
		var json = """
			{
			  "columns": [{ "name": "score", "type": "double" }],
			  "values": [[3.14]]
			}
			""";

		var results = ReadRows<double>(json);

		results.Should().HaveCount(1);
		results[0].Should().BeApproximately(3.14, 0.001);
	}

	[Test]
	public void ReadRows_SingleIntColumn_NullValue_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [{ "name": "count", "type": "integer" }],
			  "values": [[null]]
			}
			""";

		var act = () => ReadRows<int>(json);

		_ = act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_SingleNullableIntColumn_NullValue_ReturnsNull()
	{
		var json = """
			{
			  "columns": [{ "name": "count", "type": "integer" }],
			  "values": [[null]]
			}
			""";

		var results = ReadRows<int?>(json);

		results.Should().Equal([default]);
	}

	[Test]
	public void ReadRows_SingleIntColumn_MultipleRows_ReturnsAll()
	{
		var json = """
			{
			  "columns": [{ "name": "count", "type": "integer" }],
			  "values": [[1], [2], [3]]
			}
			""";

		var results = ReadRows<int>(json);

		results.Should().Equal([1, 2, 3]);
	}

	// =========================================================================
	// Property-Level JsonConverter
	// =========================================================================

	[Test]
	public void ReadRows_PropertyLevelConverter_DeserializesViaConverter()
	{
		var json = """
			{
			  "columns": [
			    { "name": "customId", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["ID-42", "test-item"]
			  ]
			}
			""";

		var results = ReadRows<CustomConverterDocument>(json);

		results.Should().HaveCount(1);
		results[0].CustomId.Should().Be(42);
		results[0].Name.Should().Be("test-item");
	}

	[Test]
	public void ReadRows_PropertyLevelConverter_MultipleRows()
	{
		var json = """
			{
			  "columns": [
			    { "name": "customId", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["ID-1", "first"],
			    ["ID-100", "second"],
			    ["ID-999", "third"]
			  ]
			}
			""";

		var results = ReadRows<CustomConverterDocument>(json);

		results.Should().HaveCount(3);
		results[0].CustomId.Should().Be(1);
		results[1].CustomId.Should().Be(100);
		results[2].CustomId.Should().Be(999);
	}

	// =========================================================================
	// Multi-Row Consistency
	// =========================================================================

	[Test]
	public void ReadRows_MixedScalarAndArrayForCollectionColumn_AllCoercedCorrectly()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "tags", "type": "keyword" }
			  ],
			  "values": [
			    ["item1", "solo-tag"],
			    ["item2", ["tag-a", "tag-b"]],
			    ["item3", "another-solo"]
			  ]
			}
			""";

		var results = ReadRows<ArrayStringPropertyModel>(json);

		results.Should().HaveCount(3);
		results[0].Tags.Should().BeEquivalentTo(["solo-tag"]);
		results[1].Tags.Should().BeEquivalentTo(["tag-a", "tag-b"]);
		results[2].Tags.Should().BeEquivalentTo(["another-solo"]);
	}

	[Test]
	public void ReadRows_MixedNullAndNonNull_EachRowCorrect()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    [10, "first"],
			    [null, "second"],
			    [30, null],
			    [null, null]
			  ]
			}
			""";

		var results = ReadRows<NullableIntModel>(json);

		results.Should().HaveCount(4);

		results[0].Value.Should().Be(10);
		results[0].Name.Should().Be("first");

		results[1].Value.Should().BeNull();
		results[1].Name.Should().Be("second");

		results[2].Value.Should().Be(30);
		results[2].Name.Should().Be(string.Empty);

		results[3].Value.Should().BeNull();
		results[3].Name.Should().Be(string.Empty);
	}

	[Test]
	public void ReadRows_MultipleRowsWithCollectionNulls_MixCorrectly()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "values", "type": "integer" }
			  ],
			  "values": [
			    ["row1", [1, 2, 3]],
			    ["row2", null],
			    ["row3", 42],
			    ["row4", [10]]
			  ]
			}
			""";

		var results = ReadRows<ListIntPropertyModel>(json);

		results.Should().HaveCount(4);
		results[0].Values.Should().BeEquivalentTo([1, 2, 3]);
		results[1].Values.Should().BeEmpty();
		results[2].Values.Should().BeEquivalentTo([42]);
		results[3].Values.Should().BeEquivalentTo([10]);
	}

	// =========================================================================
	// Metadata (ReadMetadata)
	// =========================================================================

	[Test]
	public void ReadMetadata_AllPropertiesPresent_CapturesIdAndIsRunning()
	{
		var json = """
			{
			  "id": "query-123",
			  "is_running": true,
			  "columns": [
			    { "name": "value", "type": "integer" }
			  ],
			  "values": [
			    [1]
			  ]
			}
			""";

		var (id, isRunning) = ReadMetadata(json);

		id.Should().Be("query-123");
		isRunning.Should().BeTrue();
	}

	[Test]
	public void ReadMetadata_MetadataAfterValues_OnlyCapturedBeforeValues()
	{
		// Metadata after values is not captured in the streaming path — id/is_running
		// must appear before or between columns/values to be captured without buffering.
		// The inference rule applies: columns/values present → is_running = false.
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" }
			  ],
			  "values": [
			    [1]
			  ],
			  "id": "query-456",
			  "is_running": false
			}
			""";

		var (id, isRunning) = ReadMetadata(json);

		id.Should().BeNull();
		isRunning.Should().Be(false);
	}

	[Test]
	public void ReadMetadata_MetadataSplitAroundValues_CapturesIdBeforeValues()
	{
		// id appears before values (captured), is_running appears after values (not captured).
		// Inference: columns/values present → is_running = false.
		var json = """
			{
			  "id": "query-789",
			  "columns": [
			    { "name": "value", "type": "integer" }
			  ],
			  "values": [
			    [1]
			  ],
			  "is_running": true
			}
			""";

		var (id, isRunning) = ReadMetadata(json);

		id.Should().Be("query-789");
		isRunning.Should().Be(false);
	}

	[Test]
	public void ReadMetadata_NoMetadataProperties_InfersNotRunningFromColumnsValues()
	{
		// No explicit metadata, but columns/values are present → is_running inferred as false.
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" }
			  ],
			  "values": [
			    [1]
			  ]
			}
			""";

		var (id, isRunning) = ReadMetadata(json);

		id.Should().BeNull();
		isRunning.Should().Be(false);
	}

	[Test]
	public void ReadMetadata_IsRunningFalse_CapturedCorrectly()
	{
		var json = """
			{
			  "id": "done-query",
			  "is_running": false,
			  "columns": [
			    { "name": "value", "type": "integer" }
			  ],
			  "values": [[1]]
			}
			""";

		var (id, isRunning) = ReadMetadata(json);

		id.Should().Be("done-query");
		isRunning.Should().Be(false);
	}

	// =========================================================================
	// DateTimeOffset / Guid / long
	// =========================================================================

	[Test]
	public void ReadRows_DateTimeOffsetProperty_DeserializesIsoString()
	{
		var json = """
			{
			  "columns": [
			    { "name": "timestamp", "type": "date" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["2024-06-15T10:30:00+02:00", "event1"]
			  ]
			}
			""";

		var results = ReadRows<DateTimeOffsetPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Timestamp.Should().Be(new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(2)));
		results[0].Name.Should().Be("event1");
	}

	[Test]
	public void ReadRows_GuidProperty_DeserializesGuidString()
	{
		var guid = Guid.NewGuid();
		var json = $$"""
			{
			  "columns": [
			    { "name": "id", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["{{guid}}", "item1"]
			  ]
			}
			""";

		var results = ReadRows<GuidPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Id.Should().Be(guid);
		results[0].Name.Should().Be("item1");
	}

	[Test]
	public void ReadRows_LongProperty_DeserializesLargeNumber()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "long" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    [9223372036854775807, "max-long"]
			  ]
			}
			""";

		var results = ReadRows<LongPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be(long.MaxValue);
		results[0].Name.Should().Be("max-long");
	}

	[Test]
	public void ReadRows_LongProperty_NegativeValue()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "long" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    [-9223372036854775808, "min-long"]
			  ]
			}
			""";

		var results = ReadRows<LongPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be(long.MinValue);
		results[0].Name.Should().Be("min-long");
	}

	// =========================================================================
	// Empty result set
	// =========================================================================

	[Test]
	public void ReadRows_EmptyValuesArray_ReturnsEmptyList()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": []
			}
			""";

		var results = ReadRows<NullableIntModel>(json);

		results.Should().BeEmpty();
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
		using var results = reader.ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	private static (string? Id, bool? IsRunning) ReadMetadata(string json)
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		var metadata = new JsonMetadataManager(options);
		var reader = new EsqlResponseReader(metadata);
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var results = reader.ReadRows<object>(stream);
		// Force enumeration to completion so metadata is fully captured
		_ = results.Rows.ToList();
		return (results.Id, results.IsRunning);
	}
}
