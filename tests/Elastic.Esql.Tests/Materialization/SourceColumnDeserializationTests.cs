// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

/// <summary>
/// Rows materialized from the <c>_source</c> column, which a query asks for with
/// <c>FROM index METADATA _source</c>. The document it carries is the one that was indexed, so it
/// keeps the shapes the columnar form flattens, a list of objects above all.
/// </summary>
public class SourceColumnDeserializationTests
{
	[Test]
	public void AListOfObjects_IsReadFromTheSourceColumn()
	{
		// the columns carry one value per member, [a, b] and [1, 2], with nothing to say which
		// quantity belongs to which sku; the document says it
		var json = """
			{
			  "columns": [
			    { "name": "reference", "type": "keyword" },
			    { "name": "lines.sku", "type": "keyword" },
			    { "name": "lines.quantity", "type": "integer" },
			    { "name": "_source", "type": "_source" }
			  ],
			  "values": [
			    ["R-1", ["a", "b"], [1, 2],
			     { "reference": "R-1", "lines": [ { "sku": "a", "quantity": 1 }, { "sku": "b", "quantity": 2 } ] }]
			  ]
			}
			""";

		var results = ReadRows<SourceOrder>(json);

		_ = results.Should().HaveCount(1);
		_ = results[0].Reference.Should().Be("R-1");
		_ = results[0].Lines.Should().HaveCount(2);
		_ = results[0].Lines[0].Sku.Should().Be("a");
		_ = results[0].Lines[0].Quantity.Should().Be(1);
		_ = results[0].Lines[1].Sku.Should().Be("b");
		_ = results[0].Lines[1].Quantity.Should().Be(2);
	}

	[Test]
	public void TwoObjectsSharingAValue_AreStillToldApart()
	{
		// the columns deduplicate, so lines.sku holds one value for two lines: reading the two
		// columns by position would pair the wrong quantity
		var json = """
			{
			  "columns": [
			    { "name": "reference", "type": "keyword" },
			    { "name": "lines.sku", "type": "keyword" },
			    { "name": "lines.quantity", "type": "integer" },
			    { "name": "_source", "type": "_source" }
			  ],
			  "values": [
			    ["R-2", "a", [1, 2],
			     { "reference": "R-2", "lines": [ { "sku": "a", "quantity": 1 }, { "sku": "a", "quantity": 2 } ] }]
			  ]
			}
			""";

		var results = ReadRows<SourceOrder>(json);

		_ = results.Should().HaveCount(1);
		_ = results[0].Lines.Should().HaveCount(2);
		_ = results[0].Lines[0].Quantity.Should().Be(1);
		_ = results[0].Lines[1].Quantity.Should().Be(2);
	}

	[Test]
	public void WithoutTheSourceColumn_TheColumnsAreAssembledAsBefore()
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

		_ = results.Should().HaveCount(1);
		_ = results[0].Address!.Street.Should().Be("123 Main St");
	}

	[Test]
	public void AComputedColumn_KeepsTheColumnarAssembly()
	{
		// TOTAL comes from an EVAL and has no counterpart in the indexed document, so the row is
		// assembled from the columns and the computed value survives
		var json = """
			{
			  "columns": [
			    { "name": "reference", "type": "keyword" },
			    { "name": "total", "type": "integer" },
			    { "name": "_source", "type": "_source" }
			  ],
			  "values": [
			    ["R-3", 7, { "reference": "R-3", "lines": [ { "sku": "a", "quantity": 7 } ] }]
			  ]
			}
			""";

		var results = ReadRows<SourceOrderWithTotal>(json);

		_ = results.Should().HaveCount(1);
		_ = results[0].Reference.Should().Be("R-3");
		_ = results[0].Total.Should().Be(7);
	}

	[Test]
	public void ANullSourceColumn_FallsBackToTheColumns()
	{
		var json = """
			{
			  "columns": [
			    { "name": "reference", "type": "keyword" },
			    { "name": "_source", "type": "_source" }
			  ],
			  "values": [
			    ["R-4", null]
			  ]
			}
			""";

		var results = ReadRows<SourceOrder>(json);

		_ = results.Should().HaveCount(1);
		_ = results[0].Reference.Should().Be("R-4");
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
		return reader.ReadRows<T>(stream).Rows.ToList();
	}
}
