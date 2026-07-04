// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

/// <summary>
/// Locks the observable materialization semantics that the direct-binding fast path must preserve.
/// Every test here passes against the assemble-and-deserialize pipeline and must keep passing
/// unchanged once the fast path is active.
/// </summary>
public class DirectBindingCharacterizationTests
{
	[Test]
	public void ReadRows_FlatModel_MultipleRows_MaterializesAllValues()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["first", 1],
			    ["second", 2],
			    ["third", 3]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(3);
		results[0].Value.Should().Be("first");
		results[0].Count.Should().Be(1);
		results[1].Value.Should().Be("second");
		results[1].Count.Should().Be(2);
		results[2].Value.Should().Be("third");
		results[2].Count.Should().Be(3);
	}

	[Test]
	public void ReadRows_EscapedStringCell_UnescapesCorrectly()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["quote \" backslash \\ unicode é", 1]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be("quote \" backslash \\ unicode é");
	}

	[Test]
	public void ReadRows_NullCells_LeavePropertyInitializerValues()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    [null, "first"],
			    [7, null]
			  ]
			}
			""";

		var results = ReadRows<NullableIntModel>(json);

		results.Should().HaveCount(2);
		results[0].Value.Should().BeNull();
		results[0].Name.Should().Be("first");
		results[1].Value.Should().Be(7);
		results[1].Name.Should().Be(string.Empty);
	}

	[Test]
	public void ReadRows_NonNullableValueCells_NullBecomesDefault()
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
	public void ReadRows_IntProperty_StringNumberCell_CoercedViaNumberHandling()
	{
		// Web defaults enable JsonNumberHandling.AllowReadingFromString; a string-encoded number
		// for an int property must deserialize successfully.
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["first", "42"]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be("first");
		results[0].Count.Should().Be(42);
	}

	[Test]
	public void ReadRows_ScalarStringProperty_ArrayCell_ThrowsJsonException()
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
	public void ReadRows_PropertyLevelConverter_UsesConverter()
	{
		var json = """
			{
			  "columns": [
			    { "name": "customId", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["ID-7", "item"]
			  ]
			}
			""";

		var results = ReadRows<CustomConverterDocument>(json);

		results.Should().HaveCount(1);
		results[0].CustomId.Should().Be(7);
		results[0].Name.Should().Be("item");
	}

	[Test]
	public void ReadRows_NestedColumns_MaterializesNestedObject()
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
	public void ReadRows_GuidCell_Deserializes()
	{
		var json = """
			{
			  "columns": [
			    { "name": "id", "type": "keyword" },
			    { "name": "name", "type": "keyword" }
			  ],
			  "values": [
			    ["0f8fad5b-d9cb-469f-a165-70867728950e", "item"]
			  ]
			}
			""";

		var results = ReadRows<GuidPropertyModel>(json);

		results.Should().HaveCount(1);
		results[0].Id.Should().Be(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
		results[0].Name.Should().Be("item");
	}

	[Test]
	public void ReadRows_DateTimeOffsetCell_Deserializes()
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
	public void ReadRows_UnmappedColumn_IsIgnored()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" },
			    { "name": "extra", "type": "keyword" }
			  ],
			  "values": [
			    ["first", 1, "ignored"]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be("first");
		results[0].Count.Should().Be(1);
	}

	[Test]
	public void ReadRows_UppercaseColumnNames_MatchCaseInsensitively()
	{
		// Web defaults set PropertyNameCaseInsensitive = true.
		var json = """
			{
			  "columns": [
			    { "name": "VALUE", "type": "keyword" },
			    { "name": "COUNT", "type": "integer" }
			  ],
			  "values": [
			    ["first", 1]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(1);
		results[0].Value.Should().Be("first");
		results[0].Count.Should().Be(1);
	}

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
}
