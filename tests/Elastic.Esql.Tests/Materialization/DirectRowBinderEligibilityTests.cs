// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class DirectRowBinderEligibilityTests
{
	[Test]
	public void Build_FlatScalarModel_CreatesDirectBinder()
	{
		var layout = BuildLayout<ScalarStringModel>(("value", "keyword"), ("count", "integer"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder!.Kinds.Should().Equal([DirectBinderKind.String, DirectBinderKind.Int32]);
	}

	[Test]
	public void Build_AllSupportedScalarKinds_CreatesDirectBinder()
	{
		var layout = BuildLayout<AllScalarKindsModel>(
			("text", "keyword"),
			("flag", "boolean"),
			("int32Value", "integer"),
			("int64Value", "long"),
			("doubleValue", "double"),
			("singleValue", "float"),
			("decimalValue", "double"),
			("dateTimeValue", "date"),
			("dateTimeOffsetValue", "date"),
			("guidValue", "keyword"),
			("nullableValue", "integer")
		);

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder!.Kinds.Should().Equal(
		[
			DirectBinderKind.String,
			DirectBinderKind.Bool,
			DirectBinderKind.Int32,
			DirectBinderKind.Int64,
			DirectBinderKind.Double,
			DirectBinderKind.Single,
			DirectBinderKind.Decimal,
			DirectBinderKind.DateTime,
			DirectBinderKind.DateTimeOffset,
			DirectBinderKind.Guid,
			DirectBinderKind.Int32
		]);
	}

	[Test]
	public void Build_CaseInsensitiveColumnMatch_CreatesDirectBinder()
	{
		var layout = BuildLayout<ScalarStringModel>(("VALUE", "keyword"), ("COUNT", "integer"));

		layout.DirectBinder.Should().NotBeNull();
	}

	[Test]
	public void Build_CaseMismatchWithCaseSensitiveOptions_DoesNotCreateDirectBinder()
	{
		var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
		var layout = BuildLayout<ScalarStringModel>(options, ("VALUE", "keyword"), ("COUNT", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_NestedColumns_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<PersonModel>(("name", "keyword"), ("address.street", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_CollectionProperty_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<ArrayStringPropertyModel>(("name", "keyword"), ("tags", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_PropertyLevelConverter_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<CustomConverterDocument>(("customId", "keyword"), ("name", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_GlobalConverterForPropertyType_DoesNotCreateDirectBinder()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new UnixEpochDateTimeConverter());
		var layout = BuildLayout<TimestampedModel>(options, ("name", "keyword"), ("createdAt", "date"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_ParameterizedConstructor_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<RecordProjection>(("message", "keyword"), ("statusCode", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_RequiredProperty_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<RequiredPropertyModel>(("name", "keyword"), ("count", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_TypeWithOnDeserializing_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<OnDeserializingModel>(("value", "keyword"), ("count", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_UnmappedColumn_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<ScalarStringModel>(("value", "keyword"), ("count", "integer"), ("extra", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_PrimitiveTargetType_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<int>(("count", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_EnumProperty_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<OrdinalEnumDocument>(("priority", "integer"), ("name", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_DenseVectorProperty_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<BookDocument>(("title", "keyword"), ("titleVec", "dense_vector"));

		layout.DirectBinder.Should().BeNull();
	}

	private static ColumnLayout BuildLayout<T>(params (string Name, string Type)[] columns) =>
		BuildLayout<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web), columns);

	private static ColumnLayout BuildLayout<T>(JsonSerializerOptions options, params (string Name, string Type)[] columns)
	{
		// Mirror the client's runtime resolver so reflection-based contracts resolve for the inline
		// test models; without it a bare JsonSerializerOptions has a null resolver and every layout
		// falls back to the slow path, masking the eligibility decision under test.
		options.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();

		var columnInfos = new EsqlResponseReader.ColumnInfo[columns.Length];
		for (var i = 0; i < columns.Length; i++)
			columnInfos[i] = new EsqlResponseReader.ColumnInfo(columns[i].Name, columns[i].Type);

		return ColumnLayout.Build(columnInfos, typeof(T), new JsonMetadataManager(options));
	}

	private sealed class AllScalarKindsModel
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

	private sealed class TimestampedModel
	{
		public string Name { get; set; } = string.Empty;
		public DateTime CreatedAt { get; set; }
	}

	private sealed class RequiredPropertyModel
	{
		public required string Name { get; set; }
		public int Count { get; set; }
	}

	private sealed class OnDeserializingModel : IJsonOnDeserializing
	{
		public string Value { get; set; } = string.Empty;
		public int Count { get; set; }

		public void OnDeserializing()
		{
		}
	}

	private sealed class UnixEpochDateTimeConverter : JsonConverter<DateTime>
	{
		public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			DateTime.UnixEpoch.AddSeconds(reader.GetInt64());

		public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
			writer.WriteNumberValue((long)(value - DateTime.UnixEpoch).TotalSeconds);
	}
}
