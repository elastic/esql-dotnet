// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

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
		layout.DirectBinder!.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
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
		const string json = """{"columns":[{"name":"tags","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[["a","b"],"x"],["solo","y"],[null,"z"]]}""";

		var layout = BuildLayout<TagsModel>(("tags", "keyword"), ("name", "keyword"));
		layout.DirectBinder.Should().NotBeNull();

		var rows = ReadRows<TagsModel>(json, CreateOptions());

		rows[0].Tags.Should().Equal("a", "b");
		rows[1].Tags.Should().Equal("solo");
		rows[2].Tags.Should().BeEmpty();
	}

	[Test]
	public void ReadRows_ArrayPropertyWithScalarCell_FallsBackAndStillBinds()
	{
		const string json = """{"columns":[{"name":"tags","type":"keyword"}],"values":[[["a"]],["solo"]]}""";

		var rows = ReadRows<ArrayTagsModel>(json, CreateOptions());

		rows[0].Tags.Should().Equal("a");
		rows[1].Tags.Should().Equal("solo");
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

		BuildLayout<YesNoModel>(("enabled", "keyword"), ("name", "keyword")).DirectBinder.Should().NotBeNull();
		var rows = ReadRows<YesNoModel>(json, CreateOptions());

		rows[0].Enabled.Should().BeTrue();
		rows[1].Enabled.Should().BeFalse();
	}

	[Test]
	public void ReadRows_DateOnlyProperty_Binds()
	{
		const string json = """{"columns":[{"name":"day","type":"date"}],"values":[["2024-01-02"]]}""";

		BuildLayout<DateOnlyModel>(("day", "date")).DirectBinder.Should().NotBeNull();
		ReadRows<DateOnlyModel>(json, CreateOptions())[0].Day.Should().Be(new DateOnly(2024, 1, 2));
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

	private sealed class YesNoModel
	{
		[JsonConverter(typeof(YesNoBoolConverter))]
		public bool Enabled { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	private sealed class DateOnlyModel
	{
		public DateOnly Day { get; set; }
	}

	private sealed class YesNoBoolConverter : JsonConverter<bool>
	{
		public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			reader.GetString() == "yes";

		public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value ? "yes" : "no");
	}
}
