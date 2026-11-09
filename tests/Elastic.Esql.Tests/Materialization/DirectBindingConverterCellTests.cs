// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class DirectBindingConverterCellTests
{
	[Test]
	public void Build_EnumProperty_UsesConverterKind()
	{
		var layout = BuildLayout<EnumModel>(("level", "integer"), ("name", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
	}

	[Test]
	public void ReadRows_EnumCells_BindNumericAndStringForms()
	{
		const string json = """{"columns":[{"name":"level","type":"integer"},{"name":"name","type":"keyword"}],"values":[[2,"a"],[null,"b"]]}""";

		var numeric = ReadRows<EnumModel>(json, CreateOptions());
		numeric[0].Level.Should().Be(Level.High);
		numeric[1].Level.Should().Be(Level.Low);

		const string stringJson = """{"columns":[{"name":"level","type":"keyword"},{"name":"name","type":"keyword"}],"values":[["high","a"]]}""";
		var options = CreateOptions();
		options.Converters.Add(new JsonStringEnumConverter());

		ReadRows<EnumModel>(stringJson, options)[0].Level.Should().Be(Level.High);
	}

	[Test]
	public void ReadRows_ListProperty_BindsArrayScalarAndNullCells()
	{
		const string json =
			"""{"columns":[{"name":"tags","type":"keyword"},""" +
			"""{"name":"name","type":"keyword"}],"values":[[["a","b"],"x"],["solo","y"],[null,"z"]]}""";

		var layout = BuildLayout<TagsModel>(("tags", "keyword"), ("name", "keyword"));
		layout.DirectBinder.Should().NotBeNull();

		var rows = ReadRows<TagsModel>(json, CreateOptions());

		rows[0].Tags.Should().Equal("a", "b");
		rows[1].Tags.Should().Equal("solo");
		rows[2].Tags.Should().BeEmpty();
	}

	[Test]
	public void ReadRows_ArrayPropertyWithScalarCell_BindsDirectly()
	{
		const string json = """{"columns":[{"name":"tags","type":"keyword"}],"values":[[["a"]],["solo"]]}""";

		var layout = BuildLayout<ArrayTagsModel>(("tags", "keyword"));
		layout.DirectBinder.Should().NotBeNull();

		var rows = ReadRows<ArrayTagsModel>(json, CreateOptions());

		rows[0].Tags.Should().Equal("a");
		rows[1].Tags.Should().Equal("solo");
	}

	[Test]
	public void ReadRows_ArrayPropertyWithScalarCell_BindsWithoutThrowing()
	{
		const string json = """{"columns":[{"name":"tags","type":"keyword"}],"values":[[["a"]],["solo"]]}""";

		// The handler runs on the throwing thread, so counting only this thread's exceptions keeps the
		// process-wide event from picking up other tests running in parallel.
		var testThreadId = Environment.CurrentManagedThreadId;
		var firstChanceCount = 0;
		void Count(object? sender, FirstChanceExceptionEventArgs args)
		{
			if (Environment.CurrentManagedThreadId == testThreadId)
				Interlocked.Increment(ref firstChanceCount);
		}

		AppDomain.CurrentDomain.FirstChanceException += Count;
		List<ArrayTagsModel> rows;
		try
		{
			rows = ReadRows<ArrayTagsModel>(json, CreateOptions());
		}
		finally
		{
			AppDomain.CurrentDomain.FirstChanceException -= Count;
		}

		firstChanceCount.Should().Be(0);
		rows[0].Tags.Should().Equal("a");
		rows[1].Tags.Should().Equal("solo");
	}

	[Test]
	public void ReadRows_ByteArrayProperty_BindsBase64Cell()
	{
		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"data","type":"keyword"}],"values":[["a","aGk="]]}""";

		BuildLayout<BinaryDataModel>(("name", "keyword"), ("data", "keyword")).DirectBinder.Should().NotBeNull();

		var row = ReadRows<BinaryDataModel>(json, CreateOptions())[0];

		row.Name.Should().Be("a");
		row.Data.Should().Equal([0x68, 0x69]);
	}

	[Test]
	public void ReadRows_ListPropertyWithUnparsableScalarCell_ThrowsJsonException()
	{
		const string json = """{"columns":[{"name":"numbers","type":"integer"}],"values":[["oops"]]}""";

		BuildLayout<NumbersModel>(("numbers", "integer")).DirectBinder.Should().NotBeNull();

		var act = () => ReadRows<NumbersModel>(json, CreateOptions());

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_TypeLevelStrictNumberHandling_RejectsQuotedElement()
	{
		const string json = """{"columns":[{"name":"numbers","type":"integer"},{"name":"name","type":"keyword"}],"values":[[["5"],"a"]]}""";

		// Web defaults read numbers from strings; the type opts out of that, and the opt-out has to reach the cell too.
		BuildLayout<StrictNumbersModel>(("numbers", "integer"), ("name", "keyword")).DirectBinder.Should().BeNull();

		var act = () => ReadRows<StrictNumbersModel>(json, CreateOptions());

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_IgnoreWhenReadingProperty_KeepsDefault()
	{
		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"note","type":"keyword"}],"values":[["a","sent"]]}""";

		var row = ReadRows<IgnoreWhenReadingModel>(json, CreateOptions())[0];

		row.Name.Should().Be("a");
		row.Note.Should().Be("default");
	}

	[Test]
	public void ReadRows_NestedObjectProperty_BindsObjectCell()
	{
		const string json = """{"columns":[{"name":"owner","type":"object"},{"name":"name","type":"keyword"}],"values":[[{"name":"ada"},"doc"]]}""";

		var layout = BuildLayout<NestedOwnerModel>(("owner", "object"), ("name", "keyword"));
		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder!.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);

		var row = ReadRows<NestedOwnerModel>(json, CreateOptions())[0];

		row.Owner!.Name.Should().Be("ada");
		row.Name.Should().Be("doc");
	}

	[Test]
	public void ReadRows_DictionaryProperty_BindsObjectCell()
	{
		const string json = """{"columns":[{"name":"attributes","type":"object"},{"name":"name","type":"keyword"}],"values":[[{"env":"prod"},"x"]]}""";

		BuildLayout<AttributesModel>(("attributes", "object"), ("name", "keyword")).DirectBinder.Should().NotBeNull();
		ReadRows<AttributesModel>(json, CreateOptions())[0].Attributes.Should().ContainKey("env").WhoseValue.Should().Be("prod");
	}

	[Test]
	public void ReadRows_RequiredPropertyBound_UsesFastPathAndRejectsNull()
	{
		BuildLayout<RequiredNameModel>(("name", "keyword"), ("count", "integer")).DirectBinder.Should().NotBeNull();

		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["a",1]]}""";
		ReadRows<RequiredNameModel>(json, CreateOptions())[0].Name.Should().Be("a");

		const string nullJson = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[[null,1]]}""";
		var act = () => ReadRows<RequiredNameModel>(nullJson, CreateOptions());
		act.Should().Throw<JsonException>().WithMessage("*required*");
	}

	[Test]
	public void Build_RequiredPropertyWithoutColumn_KeepsSlowPath() =>
		BuildLayout<RequiredNameModel>(("count", "integer")).DirectBinder.Should().BeNull();

	[Test]
	public void ReadRows_PropertyLevelConverter_IsHonored()
	{
		const string json = """{"columns":[{"name":"enabled","type":"keyword"},{"name":"name","type":"keyword"}],"values":[["yes","a"],["no","b"]]}""";

		var rows = ReadRows<YesNoModel>(json, CreateOptions());

		rows[0].Enabled.Should().BeTrue();
		rows[1].Enabled.Should().BeFalse();
	}

	[Test]
	public void Build_PropertyLevelConverter_UsesConverterKind()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var layout = BuildLayout<YesNoModel>(("enabled", "keyword"), ("name", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
	}

	[Test]
	public void ReadRows_PropertyLevelConverter_ReadsCellThroughThatConverter()
	{
		const string json = """{"columns":[{"name":"value","type":"integer"},{"name":"name","type":"keyword"}],"values":[[21,"a"]]}""";

		var rows = ReadRows<DoubledModel>(json, CreateOptions());

		rows.Should().ContainSingle().Which.Value.Should().Be(42);
	}

	[Test]
	public void Build_NullablePropertyLevelConverterFactory_UsesConverterKind()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var layout = BuildLayout<MaybeLevelModel>(("maybe", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Converter]);
	}

	[Test]
	public void ReadRows_NullablePropertyLevelConverterFactory_BindsStringAndNull()
	{
		const string json = """{"columns":[{"name":"maybe","type":"keyword"}],"values":[["low"],[null]]}""";

		var rows = ReadRows<MaybeLevelModel>(json, CreateOptions());

		rows.Should().HaveCount(2);
		rows[0].Maybe.Should().Be(Level.Low);
		rows[1].Maybe.Should().BeNull();
	}

	[Test]
	public void ReadRows_DateOnlyProperty_Binds()
	{
		const string json = """{"columns":[{"name":"day","type":"date"}],"values":[["2024-01-02"]]}""";

		BuildLayout<DateOnlyModel>(("day", "date")).DirectBinder.Should().NotBeNull();
		ReadRows<DateOnlyModel>(json, CreateOptions())[0].Day.Should().Be(new DateOnly(2024, 1, 2));
	}

	[Test]
	public void ReadRows_NullableDateTimeWithGlobalConverter_AppliesConverter()
	{
		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"when","type":"date"}],"values":[["a",86400]]}""";
		var options = CreateOptions();
		options.Converters.Add(new UnixEpochDateTimeConverter());

		var row = ReadRows<NullableDateModel>(json, options)[0];

		row.When.Should().Be(DateTime.UnixEpoch.AddSeconds(86400));
		row.When!.Value.Kind.Should().Be(DateTimeKind.Utc);
	}

	[Test]
	public void ReadRows_ConverterCellSplitAcrossReads_Binds()
	{
		const string json = """{"columns":[{"name":"tags","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[["a","b"],"x"],[["c"],"y"]]}""";
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(json), maxBytesPerRead: 5);
		using var results = new EsqlResponseReader(new JsonMetadataManager(CreateOptions())).ReadRows<TagsModel>(stream);

		var rows = results.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Tags.Should().Equal("a", "b");
		rows[1].Tags.Should().Equal("c");
	}

	[Test]
	[NotInParallel(nameof(CountingOwner))]
	public void ReadRows_ConverterCellRowSplitAcrossReads_RunsNestedCallbackOnce()
	{
		const string json =
			"""{"columns":[{"name":"owner","type":"object"},""" +
			"""{"name":"count","type":"integer"}],"values":[[{"name":"ada"},1],[{"name":"bob"},2]]}""";
		CountingOwner.Deserialized = 0;
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(json), maxBytesPerRead: 1);
		using var results = new EsqlResponseReader(new JsonMetadataManager(CreateOptions())).ReadRows<CountingOwnerModel>(stream);

		var rows = results.Rows.ToList();

		rows.Should().HaveCount(2);
		CountingOwner.Deserialized.Should().Be(2);
	}

	[Test]
	[NotInParallel(nameof(CountingOwner))]
	public void ReadRows_ConverterCellRowThatFallsBack_RunsNestedCallbackOnce()
	{
		// Web defaults read a quoted number, so the count cell falls back to the serializer after the owner cell.
		const string json = """{"columns":[{"name":"owner","type":"object"},{"name":"count","type":"integer"}],"values":[[{"name":"ada"},"1"]]}""";
		CountingOwner.Deserialized = 0;

		var rows = ReadRows<CountingOwnerModel>(json, CreateOptions());

		rows.Should().ContainSingle().Which.Count.Should().Be(1);
		CountingOwner.Deserialized.Should().Be(1);
	}

	[Test]
	public void ReadRows_ScalarSetterAfterConverterCell_AssignsInColumnOrder()
	{
		const string json = """{"columns":[{"name":"owner","type":"object"},{"name":"count","type":"integer"}],"values":[[{"name":"ada"},7]]}""";

		BuildLayout<ForwardingModel>(("owner", "object"), ("count", "integer")).DirectBinder.Should().NotBeNull();
		var row = ReadRows<ForwardingModel>(json, CreateOptions())[0];

		row.Owner.Name.Should().Be("ada");
		row.Count.Should().Be(7);
	}

	[Test]
	public void ReadRows_ConverterCellBeyondMaxDepth_ThrowsLikeTheSerializer()
	{
		const string json = """{"columns":[{"name":"tags","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[["a","b"],"x"]]}""";
		var options = CreateOptions();
		options.MaxDepth = 1;

		var act = () => ReadRows<TagsModel>(json, options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_ConverterReturnsNullForNonNullableProperty_ThrowsUnderRespectNullableAnnotations()
	{
		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["a",1]]}""";
		var options = CreateOptions();
		options.RespectNullableAnnotations = true;
		options.Converters.Add(new NullingStringConverter());

		BuildLayout<NonNullableNameModel>(("name", "keyword"), ("count", "integer")).DirectBinder.Should().NotBeNull();
		var act = () => ReadRows<NonNullableNameModel>(json, options);

		act.Should().Throw<JsonException>();
	}

	private static ColumnLayout BuildLayout<T>(params (string Name, string Type)[] columns)
	{
		var columnInfos = new EsqlResponseReader.ColumnInfo[columns.Length];
		for (var i = 0; i < columns.Length; i++)
			columnInfos[i] = new EsqlResponseReader.ColumnInfo(columns[i].Name, columns[i].Type);

		return ColumnLayout.Build(columnInfos, typeof(T), new JsonMetadataManager(CreateOptions()));
	}

	private static List<T> ReadRows<T>(string json, JsonSerializerOptions options)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var results = new EsqlResponseReader(new JsonMetadataManager(options)).ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	private static JsonSerializerOptions CreateOptions() =>
		new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

	private enum Level { Low, Medium, High }

	private sealed class EnumModel
	{
		public Level Level { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	private sealed class TagsModel
	{
		public List<string> Tags { get; set; } = [];
		public string Name { get; set; } = string.Empty;
	}

	private sealed class ArrayTagsModel
	{
		public string[] Tags { get; set; } = [];
	}

	private sealed class NumbersModel
	{
		public List<int> Numbers { get; set; } = [];
	}

	private sealed class BinaryDataModel
	{
		public string Name { get; set; } = string.Empty;
		public byte[] Data { get; set; } = [];
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

	private sealed class NestedOwnerModel
	{
		public Owner? Owner { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	private sealed class Owner
	{
		public string Name { get; set; } = string.Empty;
		public int Count { get; set; }
	}

	private sealed class CountingOwnerModel
	{
		public CountingOwner? Owner { get; set; }
		public int Count { get; set; }
	}

	private sealed class CountingOwner : IJsonOnDeserialized
	{
		public static int Deserialized;

		public string Name { get; set; } = string.Empty;

		public void OnDeserialized() => Deserialized++;
	}

	private sealed class ForwardingModel
	{
		public Owner Owner { get; set; } = new();

		public int Count
		{
			get => Owner.Count;
			set => Owner.Count = value;
		}
	}

	private sealed class AttributesModel
	{
		public Dictionary<string, string>? Attributes { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	private sealed class RequiredNameModel
	{
		public required string Name { get; set; }
		public int Count { get; set; }
	}

	private sealed class NonNullableNameModel
	{
		public string Name { get; set; } = string.Empty;
		public int Count { get; set; }
	}

	private sealed class YesNoModel
	{
		[JsonConverter(typeof(YesNoBoolConverter))]
		public bool Enabled { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	private sealed class DoubledModel
	{
		[JsonConverter(typeof(DoublingIntConverter))]
		public int Value { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	private sealed class MaybeLevelModel
	{
		[JsonConverter(typeof(JsonStringEnumConverter))]
		public Level? Maybe { get; set; }
	}

	private sealed class DateOnlyModel
	{
		public DateOnly Day { get; set; }
	}

	private sealed class NullableDateModel
	{
		public string Name { get; set; } = string.Empty;
		public DateTime? When { get; set; }
	}

	private sealed class YesNoBoolConverter : JsonConverter<bool>
	{
		public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			reader.GetString() == "yes";

		public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value ? "yes" : "no");
	}

	private sealed class DoublingIntConverter : JsonConverter<int>
	{
		public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			reader.GetInt32() * 2;

		public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
			writer.WriteNumberValue(value);
	}

	private sealed class UnixEpochDateTimeConverter : JsonConverter<DateTime>
	{
		public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			DateTime.UnixEpoch.AddSeconds(reader.GetInt64());

		public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
			writer.WriteNumberValue((long)(value - DateTime.UnixEpoch).TotalSeconds);
	}

	private sealed class NullingStringConverter : JsonConverter<string>
	{
		public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			reader.Skip();
			return null;
		}

		public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value);
	}
}
