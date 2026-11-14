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
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.String, DirectBinderKind.Int32]);
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
		layout.DirectBinder.Kinds.Should().Equal(
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
	public void Build_NestedColumns_CreatesDirectBinder()
	{
		var layout = BuildLayout<PersonModel>(("name", "keyword"), ("address.street", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.String, DirectBinderKind.String]);
	}

	[Test]
	public void Build_TwoBranchesOfOneNestedType_CreatesDirectBinder()
	{
		// Read-only options share one contract per type, as on every read, so both branches meet the same property infos.
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
		options.MakeReadOnly();

		var layout = BuildLayout<TwoAddressesModel>(options, ("home.city", "keyword"), ("work.city", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
	}

	[Test]
	public void Build_BranchPropertyWithConverter_DoesNotCreateDirectBinder() =>
		BuildLayout<ConvertedAddressModel>(("name", "keyword"), ("address.city", "keyword")).DirectBinder.Should().BeNull();

	[Test]
	public void Build_BranchPropertyWithPopulateHandling_DoesNotCreateDirectBinder() =>
		BuildLayout<PopulatedAddressModel>(("name", "keyword"), ("address.city", "keyword")).DirectBinder.Should().BeNull();

	[Test]
	public void Build_BranchPropertyWithoutSetter_DoesNotCreateDirectBinder() =>
		BuildLayout<GetOnlyAddressModel>(("name", "keyword"), ("address.city", "keyword")).DirectBinder.Should().BeNull();

	[Test]
	public void Build_DollarNamedBranchUnderPreserve_DoesNotCreateDirectBinder()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.Preserve };

		var layout = BuildLayout<DollarAddressModel>(options, ("name", "keyword"), ("$meta.city", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_BranchThroughEmptyJsonName_DoesNotCreateDirectBinder() =>
		BuildLayout<EmptyNameOuterModel>(("name", "keyword"), ("outer..city", "keyword")).DirectBinder.Should().BeNull();

	[Test]
	public void Build_LeafAndBranchOnOneProperty_DoesNotCreateDirectBinder()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			AllowDuplicateProperties = false,
			TypeInfoResolver = new DefaultJsonTypeInfoResolver()
		};
		var serializerRead = () => JsonSerializer.Deserialize<PersonModel>("""{"address":{"street":"s"},"address":{"city":"c"}}""", options);
		serializerRead.Should().Throw<JsonException>();

		var layout = BuildLayout<PersonModel>(options, ("address", "object"), ("address.city", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_NestedTypeWithParameterizedConstructor_DoesNotCreateDirectBinder() =>
		BuildLayout<RecordAddressModel>(("name", "keyword"), ("address.city", "keyword")).DirectBinder.Should().BeNull();

	[Test]
	public void Build_NestedTypeWithOnDeserialized_DoesNotCreateDirectBinder() =>
		BuildLayout<CallbackAddressModel>(("name", "keyword"), ("address.city", "keyword")).DirectBinder.Should().BeNull();

	[Test]
	public void Build_NestedTypeWithTypeLevelNumberHandling_DoesNotCreateDirectBinder() =>
		BuildLayout<StrictStatsModel>(("name", "keyword"), ("stats.numbers", "integer")).DirectBinder.Should().BeNull();

	[Test]
	public void Build_NestedRequiredMemberWithColumn_CreatesDirectBinder() =>
		BuildLayout<RequiredStreetModel>(("name", "keyword"), ("address.street", "keyword"), ("address.city", "keyword")).DirectBinder.Should().NotBeNull();

	[Test]
	public void Build_NestedRequiredMemberWithoutColumn_DoesNotCreateDirectBinder() =>
		BuildLayout<RequiredStreetModel>(("name", "keyword"), ("address.city", "keyword")).DirectBinder.Should().BeNull();

	[Test]
	public void Build_ConverterCellUnderBranchWithoutACellLevel_DoesNotCreateDirectBinder()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 2 };

		var layout = BuildLayout<NestedNumbersModel>(options, ("holder.numbers", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_StringArrayProperty_UsesStringArrayKind()
	{
		var layout = BuildLayout<ArrayStringPropertyModel>(("name", "keyword"), ("tags", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.String, DirectBinderKind.StringArray]);
	}

	[Test]
	public void Build_GlobalConverterForPropertyType_UsesConverterKind()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new UnixEpochDateTimeConverter());
		var layout = BuildLayout<TimestampedModel>(options, ("name", "keyword"), ("createdAt", "date"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.String, DirectBinderKind.Converter]);
	}

	[Test]
	public void Build_GlobalConverterForUnderlyingType_UsesConverterKind()
	{
		// GetTypeInfo(DateTime?) yields the built-in nullable converter wrapping the user's DateTime
		// converter, so the cell contract applies it exactly as the serializer would.
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new UnixEpochDateTimeConverter());
		var layout = BuildLayout<NullableDateModel>(options, ("name", "keyword"), ("when", "date"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.String, DirectBinderKind.Converter]);
	}

	[Test]
	public void Build_ParameterizedConstructor_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<RecordProjection>(("message", "keyword"), ("statusCode", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_RequiredPropertyWithColumn_CreatesDirectBinder()
	{
		var layout = BuildLayout<RequiredPropertyModel>(("name", "keyword"), ("count", "integer"));

		layout.DirectBinder.Should().NotBeNull();
	}

	[Test]
	public void Build_RequiredPropertyWithoutColumn_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<RequiredPropertyModel>(("count", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_TypeWithOnDeserializing_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<OnDeserializingModel>(("value", "keyword"), ("count", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_TypeWithPopulateCreationHandling_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<PopulateModel>(("tags", "keyword"), ("name", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_TypeLevelNumberHandling_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<StrictNumbersModel>(("numbers", "integer"), ("name", "keyword"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void Build_IgnoreWhenReadingProperty_DoesNotCreateDirectBinder()
	{
		var layout = BuildLayout<IgnoreWhenReadingModel>(("name", "keyword"), ("note", "keyword"));

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
	public void Build_EnumPropertyOnKeywordColumn_UsesConverterKind()
	{
		var layout = BuildLayout<OrdinalEnumDocument>(("priority", "keyword"), ("name", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
	}

	[Test]
	public void Build_DenseVectorProperty_UsesConverterKind()
	{
		var layout = BuildLayout<BookDocument>(("title", "keyword"), ("titleVec", "dense_vector"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds[1].Should().Be(DirectBinderKind.Converter);
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

	private sealed class NullableDateModel
	{
		public string Name { get; set; } = string.Empty;
		public DateTime? When { get; set; }
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

	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	private sealed class PopulateModel
	{
		public List<string> Tags { get; set; } = ["seed"];
		public string Name { get; set; } = string.Empty;
	}

	[JsonNumberHandling(JsonNumberHandling.Strict)]
	private sealed class StrictNumbersModel
	{
		public List<int> Numbers { get; set; } = [];
		public string Name { get; set; } = string.Empty;
	}

	private sealed class IgnoreWhenReadingModel
	{
		public string Name { get; set; } = string.Empty;

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
		public string Note { get; set; } = "default";
	}

	private sealed class OnDeserializingModel : IJsonOnDeserializing
	{
		public string Value { get; set; } = string.Empty;
		public int Count { get; set; }

		public void OnDeserializing()
		{
		}
	}

	private sealed class TwoAddressesModel
	{
		public AddressModel? Home { get; set; }
		public AddressModel? Work { get; set; }
	}

	private sealed class ConvertedAddressModel
	{
		public string Name { get; set; } = string.Empty;

		[JsonConverter(typeof(UpperCaseCityConverter))]
		public AddressModel? Address { get; set; }
	}

	private sealed class PopulatedAddressModel
	{
		public string Name { get; set; } = string.Empty;

		[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
		public AddressModel Address { get; set; } = new();
	}

	private sealed class GetOnlyAddressModel
	{
		public string Name { get; set; } = string.Empty;
		public AddressModel Address { get; } = new();
	}

	private sealed class DollarAddressModel
	{
		public string Name { get; set; } = string.Empty;

		[JsonPropertyName("$meta")]
		public AddressModel? Meta { get; set; }
	}

	private sealed class RecordAddressModel
	{
		public string Name { get; set; } = string.Empty;
		public AddressRecord? Address { get; set; }
	}

	private sealed record AddressRecord(string Street, string City);

	private sealed class CallbackAddressModel
	{
		public string Name { get; set; } = string.Empty;
		public CallbackAddress? Address { get; set; }
	}

	private sealed class CallbackAddress : IJsonOnDeserialized
	{
		public string City { get; set; } = string.Empty;

		public void OnDeserialized()
		{
		}
	}

	private sealed class StrictStatsModel
	{
		public string Name { get; set; } = string.Empty;
		public StrictStats? Stats { get; set; }
	}

	[JsonNumberHandling(JsonNumberHandling.Strict)]
	private sealed class StrictStats
	{
		public List<int> Numbers { get; set; } = [];
	}

	private sealed class RequiredStreetModel
	{
		public string Name { get; set; } = string.Empty;
		public RequiredStreetAddress? Address { get; set; }
	}

	private sealed class RequiredStreetAddress
	{
		public required string Street { get; set; }
		public string City { get; set; } = string.Empty;
	}

	private sealed class NestedNumbersModel
	{
		public NumbersHolder? Holder { get; set; }
	}

	private sealed class NumbersHolder
	{
		public List<List<int>> Numbers { get; set; } = [];
	}

	private sealed class EmptyNameOuterModel
	{
		public string Name { get; set; } = string.Empty;
		public EmptyNameHolder? Outer { get; set; }
	}

	private sealed class EmptyNameHolder
	{
		[JsonPropertyName("")]
		public AddressModel? Address { get; set; }
	}

	private sealed class UnixEpochDateTimeConverter : JsonConverter<DateTime>
	{
		public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			DateTime.UnixEpoch.AddSeconds(reader.GetInt64());

		public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
			writer.WriteNumberValue((long)(value - DateTime.UnixEpoch).TotalSeconds);
	}

	private sealed class UpperCaseCityConverter : JsonConverter<AddressModel>
	{
		public override AddressModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var address = JsonSerializer.Deserialize<AddressModel>(ref reader, options);
			address?.City = address.City.ToUpperInvariant();
			return address;
		}

		public override void Write(Utf8JsonWriter writer, AddressModel value, JsonSerializerOptions options) =>
			JsonSerializer.Serialize(writer, value, options);
	}
}
