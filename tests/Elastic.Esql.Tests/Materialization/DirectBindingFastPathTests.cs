// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class DirectBindingFastPathTests
{
	[Test]
	public void TryBindRowDirect_FlatRow_BindsAllValues()
	{
		var binder = CreateBinder<ScalarStringModel>(("value", "keyword"), ("count", "integer"));
		var reader = new Utf8JsonReader("[\"hello\",42]"u8);
		reader.Read();

		var result = EsqlResponseReader.TryBindRowDirect<ScalarStringModel>(ref reader, binder, out var item, out var incomplete);

		result.Should().BeTrue();
		incomplete.Should().BeFalse();
		item!.Value.Should().Be("hello");
		item.Count.Should().Be(42);
	}

	[Test]
	public void TryBindRowDirect_NullCell_LeavesInitializerValue()
	{
		var binder = CreateBinder<ScalarStringModel>(("value", "keyword"), ("count", "integer"));
		var reader = new Utf8JsonReader("[null,5]"u8);
		reader.Read();

		var result = EsqlResponseReader.TryBindRowDirect<ScalarStringModel>(ref reader, binder, out var item, out var incomplete);

		result.Should().BeTrue();
		incomplete.Should().BeFalse();
		item!.Value.Should().Be(string.Empty);
		item.Count.Should().Be(5);
	}

	[Test]
	public void TryBindRowDirect_ArrayCell_ReturnsFalseWithoutIncomplete()
	{
		var binder = CreateBinder<ScalarStringModel>(("value", "keyword"), ("count", "integer"));
		var reader = new Utf8JsonReader("[[\"a\",\"b\"],1]"u8);
		reader.Read();

		var result = EsqlResponseReader.TryBindRowDirect<ScalarStringModel>(ref reader, binder, out _, out var incomplete);

		result.Should().BeFalse();
		incomplete.Should().BeFalse();
	}

	[Test]
	public void TryBindRowDirect_StringNumberCell_ReturnsFalseWithoutIncomplete()
	{
		var binder = CreateBinder<ScalarStringModel>(("value", "keyword"), ("count", "integer"));
		var reader = new Utf8JsonReader("[\"hello\",\"42\"]"u8);
		reader.Read();

		var result = EsqlResponseReader.TryBindRowDirect<ScalarStringModel>(ref reader, binder, out _, out var incomplete);

		result.Should().BeFalse();
		incomplete.Should().BeFalse();
	}

	[Test]
	public void TryBindRowDirect_FewerCellsThanColumns_ReturnsFalseWithoutIncomplete()
	{
		var binder = CreateBinder<ScalarStringModel>(("value", "keyword"), ("count", "integer"));
		var reader = new Utf8JsonReader("[\"hello\"]"u8);
		reader.Read();

		var result = EsqlResponseReader.TryBindRowDirect<ScalarStringModel>(ref reader, binder, out _, out var incomplete);

		result.Should().BeFalse();
		incomplete.Should().BeFalse();
	}

	[Test]
	public void TryBindRowDirect_MoreCellsThanColumns_ReturnsFalseWithoutIncomplete()
	{
		var binder = CreateBinder<ScalarStringModel>(("value", "keyword"), ("count", "integer"));
		var reader = new Utf8JsonReader("[\"hello\",1,2]"u8);
		reader.Read();

		var result = EsqlResponseReader.TryBindRowDirect<ScalarStringModel>(ref reader, binder, out _, out var incomplete);

		result.Should().BeFalse();
		incomplete.Should().BeFalse();
	}

	[Test]
	public void TryBindRowDirect_TruncatedRow_ReturnsIncomplete()
	{
		var binder = CreateBinder<ScalarStringModel>(("value", "keyword"), ("count", "integer"));
		var reader = new Utf8JsonReader("[\"hello\""u8, isFinalBlock: false, default);
		reader.Read();

		var result = EsqlResponseReader.TryBindRowDirect<ScalarStringModel>(ref reader, binder, out _, out var incomplete);

		result.Should().BeFalse();
		incomplete.Should().BeTrue();
	}

	[Test]
	public void ReadRows_EligibleFlatModel_MaterializesAllRows()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["first", 1],
			    [null, 2],
			    ["third", null]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(3);
		results[0].Value.Should().Be("first");
		results[0].Count.Should().Be(1);
		results[1].Value.Should().Be(string.Empty);
		results[1].Count.Should().Be(2);
		results[2].Value.Should().Be("third");
		results[2].Count.Should().Be(0);
	}

	[Test]
	public void ReadRows_AllSupportedScalarKinds_MaterializesEveryKind()
	{
		var json = """
			{
			  "columns": [
			    { "name": "text", "type": "keyword" },
			    { "name": "flag", "type": "boolean" },
			    { "name": "int32Value", "type": "integer" },
			    { "name": "int64Value", "type": "long" },
			    { "name": "doubleValue", "type": "double" },
			    { "name": "singleValue", "type": "float" },
			    { "name": "decimalValue", "type": "double" },
			    { "name": "dateTimeValue", "type": "date" },
			    { "name": "dateTimeOffsetValue", "type": "date" },
			    { "name": "guidValue", "type": "keyword" },
			    { "name": "nullableValue", "type": "integer" }
			  ],
			  "values": [
			    ["hello", true, 42, 9999999999, 2.25, 1.5, 12.34, "2024-01-02T03:04:05Z", "2024-01-02T03:04:05+01:00", "0f8fad5b-d9cb-469f-a165-70867728950e", null]
			  ]
			}
			""";

		var results = ReadRowsReflection<AllScalarKindsDto>(json);

		results.Should().HaveCount(1);
		var dto = results[0];
		dto.Text.Should().Be("hello");
		dto.Flag.Should().BeTrue();
		dto.Int32Value.Should().Be(42);
		dto.Int64Value.Should().Be(9999999999L);
		dto.DoubleValue.Should().Be(2.25);
		dto.SingleValue.Should().Be(1.5f);
		dto.DecimalValue.Should().Be(12.34m);
		dto.DateTimeValue.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
		dto.DateTimeOffsetValue.Should().Be(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)));
		dto.GuidValue.Should().Be(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
		dto.NullableValue.Should().BeNull();
	}

	private static DirectRowBinder CreateBinder<T>(params (string Name, string Type)[] columns)
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default
			)
		};
		var columnInfos = new EsqlResponseReader.ColumnInfo[columns.Length];
		for (var i = 0; i < columns.Length; i++)
			columnInfos[i] = new EsqlResponseReader.ColumnInfo(columns[i].Name, columns[i].Type);

		var layout = ColumnLayout.Build(columnInfos, typeof(T), new JsonMetadataManager(options));
		return layout.DirectBinder!;
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

	private static List<T> ReadRowsReflection<T>(string json)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		var metadata = new JsonMetadataManager(new JsonSerializerOptions(JsonSerializerDefaults.Web));
		var reader = new EsqlResponseReader(metadata);
		using var results = reader.ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	private sealed class AllScalarKindsDto
	{
		public string Text { get; set; } = string.Empty;
		public bool Flag { get; set; }
		public int Int32Value { get; set; }
		public long Int64Value { get; set; }
		public double DoubleValue { get; set; }
		public float SingleValue { get; set; }
		public decimal DecimalValue { get; set; }
		public DateTime DateTimeValue { get; set; }
		public DateTimeOffset DateTimeOffsetValue { get; set; }
		public Guid GuidValue { get; set; }
		public int? NullableValue { get; set; }
	}
}
