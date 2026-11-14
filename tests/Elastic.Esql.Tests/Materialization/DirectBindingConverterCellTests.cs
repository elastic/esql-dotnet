// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
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
	private const string TagsColumnsJson = """{"columns":[{"name":"tags","type":"keyword"},{"name":"name","type":"keyword"}],"values":""";
	private const string LevelColumnsJson = """{"columns":[{"name":"level","type":"integer"},{"name":"name","type":"keyword"}],"values":""";

	[Test]
	public void Build_EnumPropertyOnNumericColumn_UsesEnumKind()
	{
		var layout = BuildLayout<EnumModel>(("level", "integer"), ("name", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Enum, DirectBinderKind.String]);
	}

	[Test]
	public void Build_NullableEnumUnderGlobalStringEnumConverter_UsesEnumKind()
	{
		var options = CreateOptions();
		options.Converters.Add(new JsonStringEnumConverter());

		var layout = BuildLayout<NullableLevelModel>(options, ("level", "integer"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Enum]);
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
	public void ReadRows_QuotedNumberInEnumCell_ThrowsLikeTheSerializer()
	{
		var options = CreateOptions();
		// Strict keeps the serializer's answer independent of whether its number handling reaches the enum converter.
		options.NumberHandling = JsonNumberHandling.Strict;
		var serializerRead = () => JsonSerializer.Deserialize<EnumModel>("""{"level":"2","name":"a"}""", options);
		serializerRead.Should().Throw<JsonException>();

		var act = () => ReadRows<EnumModel>(LevelColumnsJson + """[["2","a"]]}""", options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_EnumConverterRejectingNumbers_ThrowsLikeTheSerializer()
	{
		var options = CreateOptions();
		options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
		var serializerRead = () => JsonSerializer.Deserialize<EnumModel>("""{"level":2,"name":"a"}""", options);
		serializerRead.Should().Throw<JsonException>();

		var act = () => ReadRows<EnumModel>(LevelColumnsJson + """[[2,"a"]]}""", options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_EnumCellOutsideTheUnderlyingRange_ThrowsLikeTheSerializer()
	{
		var options = CreateOptions();
		var serializerRead = () => JsonSerializer.Deserialize<TinyModel>("""{"value":256}""", options);
		serializerRead.Should().Throw<JsonException>();

		var act = () => ReadRows<TinyModel>("""{"columns":[{"name":"value","type":"integer"}],"values":[[256]]}""", options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_UlongEnumCellAboveLongMaxValue_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		JsonSerializer.Deserialize<HugeModel>("""{"value":18446744073709551615}""", options)!.Value.Should().Be(Huge.Max);
		BuildLayout<HugeModel>(("value", "unsigned_long")).DirectBinder!.Kinds.Should().Equal([DirectBinderKind.Enum]);

		var rows = ReadRows<HugeModel>("""{"columns":[{"name":"value","type":"unsigned_long"}],"values":[[18446744073709551615]]}""", options);

		rows.Single().Value.Should().Be(Huge.Max);
	}

	[Test]
	public void ReadRows_EnumCellsOnStructTarget_BindLikeTheSerializer()
	{
		const string json = """{"columns":[{"name":"level","type":"integer"},{"name":"huge","type":"unsigned_long"}],"values":[[2,18446744073709551615]]}""";
		var options = CreateOptions();
		var serialized = JsonSerializer.Deserialize<EnumStructModel>("""{"level":2,"huge":18446744073709551615}""", options);
		serialized.Level.Should().Be(Level.High);
		serialized.Huge.Should().Be(Huge.Max);
		// A struct target gets no compiled setter, so both cells assign through the Enum.ToObject fallback.
		var binder = BuildLayout<EnumStructModel>(("level", "integer"), ("huge", "unsigned_long")).DirectBinder;
		binder!.Kinds.Should().Equal([DirectBinderKind.Enum, DirectBinderKind.Enum]);
		binder.TypedSetters.Should().AllSatisfy(setter => setter.Should().BeNull());

		var row = ReadRows<EnumStructModel>(json, options).Single();

		row.Level.Should().Be(Level.High);
		row.Huge.Should().Be(Huge.Max);
	}

	[Test]
	public void ReadRows_NullableEnumOnNumericColumn_BindsTheValue()
	{
		BuildLayout<NullableLevelModel>(("level", "integer")).DirectBinder!.Kinds.Should().Equal([DirectBinderKind.Enum]);

		var rows = ReadRows<NullableLevelModel>("""{"columns":[{"name":"level","type":"integer"}],"values":[[2]]}""", CreateOptions());

		rows.Single().Level.Should().Be(Level.High);
	}

	[Test]
	public void ReadRows_NullableEnumUnderWrappedPropertyConverter_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		JsonSerializer.Deserialize<WrappedLevelModel>("""{"level":1}""", options)!.Level.Should().Be(Level.High);

		var rows = ReadRows<WrappedLevelModel>("""{"columns":[{"name":"level","type":"integer"}],"values":[[1]]}""", options);

		rows.Single().Level.Should().Be(Level.High);
	}

	[Test]
	public void ReadRows_NullableEnumUnderGlobalCustomConverter_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		options.Converters.Add(new DoublingLevelConverter());
		JsonSerializer.Deserialize<NullableLevelModel>("""{"level":1}""", options)!.Level.Should().Be(Level.High);

		var rows = ReadRows<NullableLevelModel>("""{"columns":[{"name":"level","type":"integer"}],"values":[[1]]}""", options);

		rows.Single().Level.Should().Be(Level.High);
	}

	[Test]
	public void ReadRows_NullableEnumUnderRegisteredWrappingFactory_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		options.Converters.Add(new NullableDoublingLevelFactory());
		JsonSerializer.Deserialize<NullableLevelModel>("""{"level":1}""", options)!.Level.Should().Be(Level.High);

		var rows = ReadRows<NullableLevelModel>("""{"columns":[{"name":"level","type":"integer"}],"values":[[1]]}""", options);

		rows.Single().Level.Should().Be(Level.High);
	}

	[Test]
	public void ReadRows_NullableIntUnderFactoryThatChangesItsAnswer_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		options.Converters.Add(new FlippingNullableDoublingIntFactory());
		// The serializer resolves the int? contract here, on the factory's one true answer, and keeps it.
		JsonSerializer.Deserialize<NullableCountModel>("""{"count":21}""", options)!.Count.Should().Be(42);

		var rows = ReadRows<NullableCountModel>("""{"columns":[{"name":"count","type":"integer"}],"values":[[21]]}""", options);

		rows.Single().Count.Should().Be(42);
	}

	[Test]
	public void ReadRows_SingleNullableIntColumnUnderRegisteredWrappingFactory_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		options.Converters.Add(new NullableDoublingIntFactory());
		JsonSerializer.Deserialize<int?>("21", options).Should().Be(42);

		var rows = ReadRows<int?>("""{"columns":[{"name":"count","type":"integer"}],"values":[[21]]}""", options);

		rows.Should().Equal(42);
	}

	[Test]
	public void ReadRows_EnumPropertyUnderNumericThenKeywordColumn_BindsBoth()
	{
		var reader = new EsqlResponseReader(new JsonMetadataManager(CreateOptions()));

		var numeric = Read(reader, LevelColumnsJson + """[[2,"a"]]}""");
		var keyword = Read(reader, """{"columns":[{"name":"level","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[1,"b"]]}""");

		numeric.Single().Level.Should().Be(Level.High);
		keyword.Single().Level.Should().Be(Level.Medium);

		static List<EnumModel> Read(EsqlResponseReader reader, string json)
		{
			using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
			using var results = reader.ReadRows<EnumModel>(stream);
			return results.Rows.ToList();
		}
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
	public void ReadRows_ArrayPropertyWithScalarCell_BindsWithoutThrowing()
	{
		const string json = """{"columns":[{"name":"tags","type":"keyword"}],"values":[[["a"]],["solo"]]}""";

		BuildLayout<ArrayTagsModel>(("tags", "keyword")).DirectBinder.Should().NotBeNull();

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
	public void ReadRows_TwoStringArrayCells_DecodeEachOnItsOwn()
	{
		const string json = """{"columns":[{"name":"first","type":"keyword"},{"name":"second","type":"keyword"}],"values":[[["a"],["b","c"]]]}""";
		var options = CreateOptions();
		var serialized = JsonSerializer.Deserialize<TwoArraysModel>("""{"first":["a"],"second":["b","c"]}""", options)!;
		serialized.First.Should().Equal("a");
		serialized.Second.Should().Equal("b", "c");
		var binder = BuildLayout<TwoArraysModel>(("first", "keyword"), ("second", "keyword")).DirectBinder;
		binder!.Kinds.Should().Equal([DirectBinderKind.StringArray, DirectBinderKind.StringArray]);

		var row = ReadRows<TwoArraysModel>(json, options).Single();

		row.First.Should().Equal("a");
		row.Second.Should().Equal("b", "c");
	}

	[Test]
	public void ReadRows_StringListCellWithNullElement_KeepsTheNull()
	{
		var options = CreateOptions();
		JsonSerializer.Deserialize<TagsModel>("""{"tags":["a",null],"name":"x"}""", options)!.Tags.Should().Equal("a", null);

		var rows = ReadRows<TagsModel>(TagsColumnsJson + """[[["a",null],"x"]]}""", options);

		rows.Single().Tags.Should().Equal("a", null);
	}

	[Test]
	public void ReadRows_StringListCellWithEscapedElement_BindsTheUnescapedText()
	{
		var rows = ReadRows<TagsModel>(TagsColumnsJson + """[[["a\"b","é"],"x"]]}""", CreateOptions());

		rows.Single().Tags.Should().Equal("a\"b", "é");
	}

	[Test]
	public void ReadRows_StringListCellWithLoneSurrogate_ThrowsJsonException()
	{
		var act = () => ReadRows<TagsModel>(TagsColumnsJson + """[[["\ud800"],"x"]]}""", CreateOptions());

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_StringSetCell_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		JsonSerializer.Deserialize<StringSetModel>("""{"tags":["a","b","a"]}""", options)!.Tags.Should().BeEquivalentTo("a", "b");

		var rows = ReadRows<StringSetModel>("""{"columns":[{"name":"tags","type":"keyword"}],"values":[[["a","b","a"]]]}""", options);

		rows.Single().Tags.Should().BeEquivalentTo("a", "b");
	}

	[Test]
	public void ReadRows_StringListWithPropertyConverter_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		JsonSerializer.Deserialize<ReversedTagsModel>("""{"tags":["a","b"]}""", options)!.Tags.Should().Equal("b", "a");

		var rows = ReadRows<ReversedTagsModel>("""{"columns":[{"name":"tags","type":"keyword"}],"values":[[["a","b"]]]}""", options);

		rows.Single().Tags.Should().Equal("b", "a");
	}

	[Test]
	public void ReadRows_StringListWithGlobalStringConverter_BindsLikeTheSerializer()
	{
		var options = CreateOptions();
		options.Converters.Add(new UpperCaseStringConverter());
		JsonSerializer.Deserialize<TagsModel>("""{"tags":["a","b"],"name":"x"}""", options)!.Tags.Should().Equal("A", "B");

		var rows = ReadRows<TagsModel>(TagsColumnsJson + """[[["a","b"],"x"]]}""", options);

		rows.Single().Tags.Should().Equal("A", "B");
	}

	[Test]
	public void ReadRows_StringListUnderObjectConverterForString_ThrowsLikeTheSerializer()
	{
		var options = CreateOptions();
		options.Converters.Add(new ObjectInfoStringFactory());
		var serializerRead = () => JsonSerializer.Deserialize<PlainTagsModel>("""{"tags":["a"]}""", options);
		serializerRead.Should().Throw<JsonException>();

		var act = () => ReadRows<PlainTagsModel>("""{"columns":[{"name":"tags","type":"keyword"}],"values":[[["a"]]]}""", options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_StringUnderObjectConverterForString_ThrowsLikeTheSerializer()
	{
		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["a",1]]}""";
		var options = CreateOptions();
		options.Converters.Add(new ObjectInfoStringFactory());
		var serializerRead = () => JsonSerializer.Deserialize<NonNullableNameModel>("""{"name":"a","count":1}""", options);
		serializerRead.Should().Throw<JsonException>();

		var act = () => ReadRows<NonNullableNameModel>(json, options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_BareStringOnUnmarkedStringListColumn_ThrowsLikeTheSerializer()
	{
		const string json =
			"""{"columns":[{"name":"TAGS","type":"keyword"},{"name":"name","type":"keyword"}],"values":[["solo","x"]]}""";
		var options = CreateOptions();
		var serializerRead = () => JsonSerializer.Deserialize<TagsModel>("""{"TAGS":"solo","name":"x"}""", options);
		serializerRead.Should().Throw<JsonException>();
		// The property matches TAGS only case-insensitively, and the layout marks a collection column on an exact match only.
		var binder = BuildLayout<TagsModel>(("TAGS", "keyword"), ("name", "keyword")).DirectBinder;
		binder!.Kinds[0].Should().Be(DirectBinderKind.StringList);
		binder.IsCollection[0].Should().BeFalse();

		var act = () => ReadRows<TagsModel>(json, options);

		act.Should().Throw<JsonException>();
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
	public void ReadRows_BareScalarInListConverterCell_BindsWithoutFallback()
	{
		const string json = """{"columns":[{"name":"numbers","type":"integer"},{"name":"name","type":"keyword"}],"values":[[5,"x"]]}""";
		var binder = BuildLayout<CountingNumbersModel>(("numbers", "integer"), ("name", "keyword")).DirectBinder;
		binder!.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
		binder.IsCollection[0].Should().BeTrue();
		CountingNumbersModel.Constructed = 0;

		var rows = ReadRows<CountingNumbersModel>(json, CreateOptions());

		// A wrap the converter rejects sends the row to the fallback, which constructs it again with the same values.
		CountingNumbersModel.Constructed.Should().Be(1);
		rows.Single().Numbers.Should().Equal(5);
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
	public void ReadRows_ConverterCellRowWithNullRequiredMember_ThrowsJsonException()
	{
		const string json = """{"columns":[{"name":"level","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[2,null]]}""";

		var binder = BuildLayout<RequiredNameLevelModel>(("level", "keyword"), ("name", "keyword")).DirectBinder;
		binder.Should().NotBeNull();
		binder.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
		var act = () => ReadRows<RequiredNameLevelModel>(json, CreateOptions());

		act.Should().Throw<JsonException>().WithMessage("*required*");
	}

	[Test]
	public void ReadRows_ConverterCellRowWithMoreCellsThanColumns_ThrowsJsonException()
	{
		// A bind that stops before the first row's closing bracket makes the next read take that bracket for the end of
		// values, which drops the second row.
		const string json = """{"columns":[{"name":"level","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[2,"a",3],[1,"b"]]}""";

		var binder = BuildLayout<RequiredNameLevelModel>(("level", "keyword"), ("name", "keyword")).DirectBinder;
		binder.Should().NotBeNull();
		binder.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
		var act = () => ReadRows<RequiredNameLevelModel>(json, CreateOptions());

		act.Should().Throw<JsonException>().WithMessage("*more values*");
	}

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
	public void Build_PropertyLevelConverterFactory_UsesConverterKind()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var layout = BuildLayout<PlainLevelModel>(("plain", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Converter]);
	}

	[Test]
	public void Build_NullablePropertyLevelConverter_UsesConverterKind()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var layout = BuildLayout<MaybeLevelModel>(("maybe", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Converter]);
	}

	[Test]
	public void ReadRows_PropertyLevelFactoryReturningBuiltInListConverter_BindsArrayCell()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		const string json = """{"columns":[{"name":"numbers","type":"integer"}],"values":[[[7,8]]]}""";
		var options = CreateOptions();
		// The reader makes the options read-only before it builds a layout, and only read-only options cache the contract
		// whose converter the factory's GetConverter call hands back.
		options.MakeReadOnly();
		var layout = BuildLayout<DeferredListModel>(options, ("numbers", "integer"));
		layout.DirectBinder.Should().NotBeNull();

		var rows = ReadRows<DeferredListModel>(json, CreateOptions());

		rows.Should().ContainSingle().Which.Numbers.Should().Equal(7, 8);
	}

	[Test]
	public void Build_ContractResolvedBeforeTheFreeze_KeepsTheDirectBinder()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var options = new JsonSerializerOptions
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(TranslatedListContext.Default, new DefaultJsonTypeInfoResolver()),
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};
		var metadata = new JsonMetadataManager(options);
		_ = metadata.ResolvePropertyName(typeof(TranslatedListModel), typeof(TranslatedListModel).GetProperty(nameof(TranslatedListModel.Name))!);
		options.MakeReadOnly();

		var layout = BuildLayout<TranslatedListModel>(metadata, ("name", "keyword"), ("numbers", "integer"));

		layout.DirectBinder.Should().NotBeNull();
	}

	[Test]
	public void ReadRows_NullableCollectionFactoryWrappingCustomConverter_ThrowsLikeTheSerializer()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"tags","type":"integer"}],"values":[["ada",[-1]]]}""";

		BuildLayout<NullableTagsModel>(("name", "keyword"), ("tags", "integer")).DirectBinder.Should().NotBeNull();
		var act = () => ReadRows<NullableTagsModel>(json, CreateOptions());

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_NullableCollectionFactoryReturningOwnWrapper_BindsArrayCell()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"tags","type":"integer"}],"values":[["ada",[1,2]]]}""";

		// GetNullableConverter builds a new wrapper, not the instance the type's own contract carries, so the type takes the
		// fallback and binds the same values there.
		BuildLayout<OwnTagsModel>(("name", "keyword"), ("tags", "integer")).DirectBinder.Should().BeNull();
		var row = ReadRows<OwnTagsModel>(json, CreateOptions())[0];

		row.Tags.Should().NotBeNull();
		row.Tags.Value.Should().Equal([1, 2]);
	}

	[Test]
	public void ReadRows_StructuredFactoryOfAnotherKind_BindsLikeTheSerializer()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		const string json = """{"columns":[{"name":"values","type":"object"}],"values":[[{}]]}""";

		var rows = ReadRows<ObjectFactoryDictionaryModel>(json, CreateOptions());

		rows.Should().ContainSingle().Which.Values.Should().NotBeNull().And.BeEmpty();
	}

	[Test]
	public void ReadRows_NullableWrapperOfAnotherKind_ThrowsLikeTheSerializer()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		const string json = """{"columns":[{"name":"tags","type":"integer"}],"values":[[[1,2]]]}""";
		var options = CreateOptions();
		var serializerRead = () => JsonSerializer.Deserialize<ObjectWrapperTagsModel>("""{"tags":[1,2]}""", options);

		// Fails here, and not on the read below, once the serializer reads an array through the wrapped object converter.
		serializerRead.Should().Throw<JsonException>();
		var act = () => ReadRows<ObjectWrapperTagsModel>(json, options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_NullableWrapperAroundAnotherInnerConverter_ThrowsLikeTheSerializer()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		const string json = """{"columns":[{"name":"value","type":"object"}],"values":[[{"x":1,"y":2}]]}""";
		var options = CreateOptions();
		var serializerRead = () => JsonSerializer.Deserialize<ConstructedPairModel>("""{"value":{"x":1,"y":2}}""", options);

		// Fails here, and not on the read below, once the serializer reads the wrapped converter against the metadata the
		// factory built instead of the type's own.
		serializerRead.Should().Throw<NotSupportedException>();
		var act = () => ReadRows<ConstructedPairModel>(json, options);

		act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void Build_DefaultProviderOptions_KeepTheDirectBinder()
	{
		var metadata = new EsqlQueryProvider().Metadata;

		BuildLayout<CountModel>(metadata, ("count", "integer")).DirectBinder.Should().NotBeNull();
	}

	[Test]
	public void ReadRows_HandWrittenResolverWithSuppliedPropertyMetadata_ThrowsLikeTheSerializer()
	{
		const string json = """{"columns":[{"name":"value","type":"object"}],"values":[[{}]]}""";
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new SuppliedPairResolver() };
		var serializerRead = () => JsonSerializer.Deserialize<SuppliedPairModel>("""{"value":{}}""", options);

		// Fails here, and not on the read below, once the serializer reads the pair through its own contract instead of
		// the property's metadata.
		serializerRead.Should().Throw<JsonException>();
		var act = () => ReadRows<SuppliedPairModel>(json, options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_ModifierWithSuppliedScalarContract_BindsLikeTheSerializer()
	{
		const string json = """{"columns":[{"name":"count","type":"integer"}],"values":[[21]]}""";
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SupplyDoublingCount } }
		};

		JsonSerializer.Deserialize<CountModel>("""{"count":21}""", options)!.Count.Should().Be(42);
		var rows = ReadRows<CountModel>(json, options);

		rows.Should().ContainSingle().Which.Count.Should().Be(42);
	}

	[Test]
	public void ReadRows_HandWrittenContextWithSerializableAttribute_BindsLikeTheSerializer()
	{
		var options = new JsonSerializerOptions
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(new AttributedHandWrittenContext(), new DefaultJsonTypeInfoResolver()),
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};
		// The serializer reads through the supplied contract, which doubles the cell.
		JsonSerializer.Deserialize<CountModel>("""{"count":21}""", options)!.Count.Should().Be(42);

		var rows = ReadRows<CountModel>("""{"columns":[{"name":"count","type":"integer"}],"values":[[21]]}""", options);

		rows.Single().Count.Should().Be(42);
	}

	[Test]
	public void Build_CombinedResolverAddedToTheChain_KeepsTheDirectBinder()
	{
		var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
		options.TypeInfoResolverChain.Add(JsonTypeInfoResolver.Combine(MaterializationTestJsonContext.Default, new DefaultJsonTypeInfoResolver()));
		options.MakeReadOnly();

		BuildLayout<CountModel>(options, ("count", "integer")).DirectBinder.Should().NotBeNull();
	}

	[Test]
	public void ReadRows_NestedOptionsChainReplacedAfterTheFreeze_BindsLikeTheSerializer()
	{
		var inner = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SupplyDoublingCount } } };
		inner.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
		var outer = new JsonSerializerOptions { TypeInfoResolver = MaterializationTestJsonContext.Default, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
		outer.TypeInfoResolverChain.Add(inner.TypeInfoResolver!);
		outer.MakeReadOnly();
		// The serializer caches the doubled contract on the outer options before the inner parts change.
		JsonSerializer.Deserialize<CountModel>("""{"count":21}""", outer)!.Count.Should().Be(42);
		inner.TypeInfoResolverChain.Clear();
		inner.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
		inner.MakeReadOnly();

		var rows = ReadRows<CountModel>("""{"columns":[{"name":"count","type":"integer"}],"values":[[21]]}""", outer);

		rows.Single().Count.Should().Be(42);
	}

	[Test]
	public void ReadRows_AssignedOptionsChainReplacedAfterTheFreeze_BindsLikeTheSerializer()
	{
		var inner = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SupplyDoublingCount } } };
		inner.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
		var outer = new JsonSerializerOptions { TypeInfoResolver = inner.TypeInfoResolver, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
		outer.MakeReadOnly();
		// The serializer caches the doubled contract on the outer options before the inner parts change.
		JsonSerializer.Deserialize<CountModel>("""{"count":21}""", outer)!.Count.Should().Be(42);
		inner.TypeInfoResolverChain.Clear();
		inner.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
		inner.MakeReadOnly();

		var rows = ReadRows<CountModel>("""{"columns":[{"name":"count","type":"integer"}],"values":[[21]]}""", outer);

		rows.Single().Count.Should().Be(42);
	}

	[Test]
	public void Build_PropertyLevelCustomListConverter_UsesConverterKind()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var layout = BuildLayout<CustomListModel>(("numbers", "integer"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.Kinds.Should().Equal([DirectBinderKind.Converter]);
	}

	[Test]
	public void ReadRows_PropertyLevelStringEnumFactory_BindsWithoutThrowing()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		const string json = """{"columns":[{"name":"plain","type":"keyword"}],"values":[["high"]]}""";

		BuildLayout<PlainLevelModel>(("plain", "keyword")).DirectBinder.Should().NotBeNull();

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
		List<PlainLevelModel> rows;
		try
		{
			rows = ReadRows<PlainLevelModel>(json, CreateOptions());
		}
		finally
		{
			AppDomain.CurrentDomain.FirstChanceException -= Count;
		}

		firstChanceCount.Should().Be(0);
		rows.Should().ContainSingle().Which.Plain.Should().Be(Level.High);
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
	public void ReadRows_StringListRowSplitAcrossReads_Binds()
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
	public void ReadRows_StringListRowSplitAcrossReads_ConstructsOncePerRow()
	{
		var binder = BuildLayout<CountingTagsModel>(("tags", "keyword"), ("name", "keyword")).DirectBinder;
		binder!.Kinds.Should().Equal([DirectBinderKind.StringList, DirectBinderKind.String]);
		CountingTagsModel.Constructed = 0;
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(TagsColumnsJson + """[[["a","b"],"x"],[["c"],"y"]]}"""), maxBytesPerRead: 1);
		using var results = new EsqlResponseReader(new JsonMetadataManager(CreateOptions())).ReadRows<CountingTagsModel>(stream);

		var rows = results.Rows.ToList();

		rows.Should().HaveCount(2);
		CountingTagsModel.Constructed.Should().Be(2);
	}

	[Test]
	public void ReadRows_EnumRowSplitAcrossReads_ConstructsOncePerRow()
	{
		var binder = BuildLayout<CountingLevelModel>(("level", "integer"), ("name", "keyword")).DirectBinder;
		binder!.Kinds.Should().Equal([DirectBinderKind.Enum, DirectBinderKind.String]);
		CountingLevelModel.Constructed = 0;
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(LevelColumnsJson + """[[2,"x"],[1,"y"]]}"""), maxBytesPerRead: 1);
		using var results = new EsqlResponseReader(new JsonMetadataManager(CreateOptions())).ReadRows<CountingLevelModel>(stream);

		var rows = results.Rows.ToList();

		rows.Should().HaveCount(2);
		CountingLevelModel.Constructed.Should().Be(2);
	}

#if NET10_0_OR_GREATER
	[Test]
	public async Task ReadRowsAsync_ConverterCellRowAcrossPipeSegments_BindsEachCell()
	{
		// Longer than a pipe segment, so the string decode reads a name whose slice spans segments.
		var longName = new string('x', 9000);
		var json = $$"""{"columns":[{"name":"level","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[2,"{{longName}}"]]}""";

		var binder = BuildLayout<EnumModel>(("level", "keyword"), ("name", "keyword")).DirectBinder;
		binder.Should().NotBeNull();
		binder.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
		var pipe = new Pipe();
		await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(json));
		await pipe.Writer.CompleteAsync();

		await using var results = await new EsqlResponseReader(new JsonMetadataManager(CreateOptions())).ReadRowsAsync<EnumModel>(pipe.Reader);
		var rows = new List<EnumModel>();
		await foreach (var row in results.Rows)
			rows.Add(row);

		var single = rows.Should().ContainSingle().Subject;
		single.Level.Should().Be(Level.High);
		single.Name.Should().Be(longName);
	}
#endif

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
	[NotInParallel(nameof(CountingOwner))]
	public void ReadRows_ConverterCellRowWithLoneSurrogateStringCell_RunsNestedCallbackOnce()
	{
		const string json = """{"columns":[{"name":"owner","type":"object"},{"name":"name","type":"keyword"}],"values":[[{"name":"ada"},"\ud800"]]}""";
		CountingOwner.Deserialized = 0;

		BuildLayout<CountingOwnerStringModel>(("owner", "object"), ("name", "keyword")).DirectBinder.Should().NotBeNull();
		var act = () => ReadRows<CountingOwnerStringModel>(json, CreateOptions());

		act.Should().Throw<JsonException>();
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
	public void ReadRows_ConverterCellRowWithEveryScalarKind_BindsEachKind()
	{
		const string json =
			"""{"columns":[{"name":"level","type":"keyword"},{"name":"flag","type":"boolean"},{"name":"created","type":"date"},""" +
			"""{"name":"seen","type":"date"},{"name":"id","type":"keyword"},{"name":"name","type":"keyword"},""" +
			"""{"name":"small","type":"integer"},{"name":"big","type":"long"},{"name":"ratio","type":"double"},""" +
			"""{"name":"weight","type":"double"},{"name":"amount","type":"double"}],"values":""" +
			"""[[2,true,"2024-01-02T03:04:05Z","2024-05-06T07:08:09+02:00","0f8fad5b-d9cb-469f-a165-70867728950e","doc",""" +
			"""123456,9000000000,2.5,1.25,12.345]]}""";

		var binder = BuildLayout<EveryKindModel>(
			("level", "keyword"),
			("flag", "boolean"),
			("created", "date"),
			("seen", "date"),
			("id", "keyword"),
			("name", "keyword"),
			("small", "integer"),
			("big", "long"),
			("ratio", "double"),
			("weight", "double"),
			("amount", "double")
		).DirectBinder;
		binder.Should().NotBeNull();
		binder.ValidatedCellCount.Should().Be(1);

		var row = ReadRows<EveryKindModel>(json, CreateOptions())[0];

		row.Level.Should().Be(Level.High);
		row.Flag.Should().BeTrue();
		row.Created.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
		row.Created.Kind.Should().Be(DateTimeKind.Utc);
		row.Seen.Should().BeExactly(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)));
		row.Id.Should().Be(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"));
		row.Name.Should().Be("doc");
		row.Small.Should().Be(123456);
		row.Big.Should().Be(9000000000L);
		row.Ratio.Should().Be(2.5);
		row.Weight.Should().Be(1.25f);
		row.Amount.Should().Be(12.345m);
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
	public void ReadRows_PreserveReferencesWithDuplicateIdAcrossCells_ThrowsJsonException()
	{
		const string json =
			"""{"columns":[{"name":"owner","type":"object"},""" +
			"""{"name":"other","type":"object"}],"values":[[{"$id":"1","name":"a"},{"$id":"1","name":"b"}]]}""";
		var options = CreateOptions();
		options.ReferenceHandler = ReferenceHandler.Preserve;

		var act = () => ReadRows<TwoOwnersModel>(json, options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void Build_IgnoreCyclesWithConverterCell_CreatesDirectBinder()
	{
		var options = CreateOptions();
		options.ReferenceHandler = ReferenceHandler.IgnoreCycles;

		var layout = BuildLayout<CountingOwnerModel>(options, ("owner", "object"), ("count", "integer"));

		layout.DirectBinder.Should().NotBeNull();
	}

	[Test]
	public void Build_PreserveReferencesWithScalarCellsOnly_CreatesDirectBinder()
	{
		var options = CreateOptions();
		options.ReferenceHandler = ReferenceHandler.Preserve;

		var layout = BuildLayout<NonNullableNameModel>(options, ("name", "keyword"), ("count", "integer"));

		layout.DirectBinder.Should().NotBeNull();
	}

	[Test]
	public void Build_CustomReferenceHandlerWithConverterCell_KeepsFallback()
	{
		var options = CreateOptions();
		options.ReferenceHandler = new PreservingReferenceHandler();

		var layout = BuildLayout<CountingOwnerModel>(options, ("owner", "object"), ("count", "integer"));

		layout.DirectBinder.Should().BeNull();
	}

	[Test]
	public void ReadRows_ConverterReturnsNullForNonNullableProperty_ThrowsUnderRespectNullableAnnotations()
	{
		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["a",1]]}""";
		var options = CreateOptions();
		options.RespectNullableAnnotations = true;
		options.Converters.Add(new NullingStringConverter());

		var binder = BuildLayout<NonNullableNameModel>(options, ("name", "keyword"), ("count", "integer")).DirectBinder;
		binder.Should().NotBeNull();
		binder.Kinds[0].Should().Be(DirectBinderKind.Converter);
		var act = () => ReadRows<NonNullableNameModel>(json, options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_LoneSurrogateStringCell_ThrowsJsonException()
	{
		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["\ud800",1]]}""";

		var binder = BuildLayout<NonNullableNameModel>(("name", "keyword"), ("count", "integer")).DirectBinder;
		binder.Should().NotBeNull();
		binder.Kinds[0].Should().Be(DirectBinderKind.String);
		var act = () => ReadRows<NonNullableNameModel>(json, CreateOptions());

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_ConverterCellRowWithLoneSurrogateStringCell_ThrowsJsonException()
	{
		const string json = """{"columns":[{"name":"level","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[2,"\ud800"]]}""";

		var binder = BuildLayout<EnumModel>(("level", "keyword"), ("name", "keyword")).DirectBinder;
		binder.Should().NotBeNull();
		binder.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.String]);
		var act = () => ReadRows<EnumModel>(json, CreateOptions());

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_InvalidUtf8InPlainStringCell_ThrowsLikeTheSerializer()
	{
		var options = CreateOptions();
		byte[] document = [.. """{"level":2,"name":"a"""u8, 0xFF, .. "\"}"u8];
		var serializerRead = () => JsonSerializer.Deserialize<EnumModel>(document, options);
		serializerRead.Should().Throw<JsonException>();
		BuildLayout<EnumModel>(("level", "integer"), ("name", "keyword")).DirectBinder!.Kinds.Should().Equal([DirectBinderKind.Enum, DirectBinderKind.String]);
		byte[] json = [.. Encoding.UTF8.GetBytes(LevelColumnsJson + "[[2,\"a"), 0xFF, .. "\"]]}"u8];
		using var stream = new MemoryStream(json);
		using var results = new EsqlResponseReader(new JsonMetadataManager(options)).ReadRows<EnumModel>(stream);

		var act = () => results.Rows.ToList();

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_ConverterCellRowWithLoneSurrogateDateTimeCell_ThrowsJsonException()
	{
		const string json = """{"columns":[{"name":"level","type":"keyword"},{"name":"created","type":"date"}],"values":[[2,"2024-01-02\ud800"]]}""";

		var binder = BuildLayout<EveryKindModel>(("level", "keyword"), ("created", "date")).DirectBinder;
		binder.Should().NotBeNull();
		binder.Kinds.Should().Equal([DirectBinderKind.Converter, DirectBinderKind.DateTime]);
		var act = () => ReadRows<EveryKindModel>(json, CreateOptions());

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_ConverterCellRowWithNullCellBeforeStringCell_BindsTheString()
	{
		const string json = """{"columns":[{"name":"level","type":"keyword"},{"name":"name","type":"keyword"}],"values":[[null,"b"]]}""";

		BuildLayout<EnumModel>(("level", "keyword"), ("name", "keyword")).DirectBinder.Should().NotBeNull();
		var rows = ReadRows<EnumModel>(json, CreateOptions());

		rows[0].Name.Should().Be("b");
	}

	[Test]
	public void ReadRows_SurrogatePairStringCell_BindsDirectly()
	{
		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["\ud83d\ude00",1]]}""";
		CountingNameModel.Constructed = 0;

		var binder = BuildLayout<CountingNameModel>(("name", "keyword"), ("count", "integer")).DirectBinder;
		binder.Should().NotBeNull();
		binder.Kinds[0].Should().Be(DirectBinderKind.String);
		var rows = ReadRows<CountingNameModel>(json, CreateOptions());

		// The direct path constructs the row before it reads the cells, and a fallback constructs it again.
		CountingNameModel.Constructed.Should().Be(1);
		rows.Should().ContainSingle().Which.Name.Should().Be("\ud83d\ude00");
	}

	private static ColumnLayout BuildLayout<T>(params (string Name, string Type)[] columns) => BuildLayout<T>(CreateOptions(), columns);

	private static ColumnLayout BuildLayout<T>(JsonSerializerOptions options, params (string Name, string Type)[] columns) =>
		BuildLayout<T>(new JsonMetadataManager(options), columns);

	private static ColumnLayout BuildLayout<T>(JsonMetadataManager metadata, params (string Name, string Type)[] columns)
	{
		var columnInfos = new EsqlResponseReader.ColumnInfo[columns.Length];
		for (var i = 0; i < columns.Length; i++)
			columnInfos[i] = new EsqlResponseReader.ColumnInfo(columns[i].Name, columns[i].Type);

		return ColumnLayout.Build(columnInfos, typeof(T), metadata);
	}

	private static List<T> ReadRows<T>(string json, JsonSerializerOptions options)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var results = new EsqlResponseReader(new JsonMetadataManager(options)).ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	private static JsonSerializerOptions CreateOptions() =>
		new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

	// Swaps the reflected count for a property that carries a doubling contract of its own, which only the serializer,
	// reading through the property's metadata, applies.
	private static void SupplyDoublingCount(JsonTypeInfo typeInfo)
	{
		if (typeInfo.Type != typeof(CountModel))
			return;

		var reflected = typeInfo.Properties.First(property => property.Name == "count");
		_ = typeInfo.Properties.Remove(reflected);
		typeInfo.Properties.Add(CreateDoublingCountProperty(typeInfo.Options));
	}

	internal static JsonTypeInfo CreateDoublingCountInfo(JsonSerializerOptions options)
	{
		var typeInfo = JsonTypeInfo.CreateJsonTypeInfo<CountModel>(options);
		typeInfo.CreateObject = static () => new CountModel();
		typeInfo.Properties.Add(CreateDoublingCountProperty(options));
		return typeInfo;
	}

	private static JsonPropertyInfo CreateDoublingCountProperty(JsonSerializerOptions options) =>
		JsonMetadataServices.CreatePropertyInfo<int>(
			options,
			new JsonPropertyInfoValues<int>
			{
				IsProperty = true,
				IsPublic = true,
				DeclaringType = typeof(CountModel),
				PropertyName = nameof(CountModel.Count),
				JsonPropertyName = "count",
				PropertyTypeInfo = JsonMetadataServices.CreateValueInfo<int>(options, new DoublingIntConverter()),
				Getter = static model => ((CountModel)model).Count,
				Setter = static (model, count) => ((CountModel)model).Count = count
			}
		);

	private enum Level { Low, Medium, High }

	private sealed class EnumModel
	{
		public Level Level { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	private sealed class NullableLevelModel
	{
		public Level? Level { get; set; }
	}

	private sealed class WrappedLevelModel
	{
		[JsonConverter(typeof(NullableDoublingLevelFactory))]
		public Level? Level { get; set; }
	}

	private enum Tiny : byte { Zero, One }

	private sealed class TinyModel
	{
		public Tiny Value { get; set; }
	}

	private enum Huge : ulong { Zero, Max = ulong.MaxValue }

	private sealed class HugeModel
	{
		public Huge Value { get; set; }
	}

	private struct EnumStructModel
	{
		// Nullable, so a fallback that boxes with the nullable type in place of the enum type fails.
		public Level? Level { get; set; }
		public Huge Huge { get; set; }
	}

	private sealed class CountingLevelModel
	{
		[ThreadStatic]
		public static int Constructed;

		public CountingLevelModel() => Constructed++;

		public Level Level { get; set; }
		public string Name { get; set; } = string.Empty;
	}

	private sealed class TagsModel
	{
		public List<string> Tags { get; set; } = [];
		public string Name { get; set; } = string.Empty;
	}

	private sealed class PlainTagsModel
	{
		public List<string> Tags { get; set; } = [];
	}

	private sealed class CountingTagsModel
	{
		[ThreadStatic]
		public static int Constructed;

		public CountingTagsModel() => Constructed++;

		public List<string> Tags { get; set; } = [];
		public string Name { get; set; } = string.Empty;
	}

	private sealed class ReversedTagsModel
	{
		[JsonConverter(typeof(ReversingStringListConverter))]
		public List<string> Tags { get; set; } = [];
	}

	private sealed class StringSetModel
	{
		public HashSet<string> Tags { get; set; } = [];
	}

	private sealed class ArrayTagsModel
	{
		public string[] Tags { get; set; } = [];
	}

	private sealed class TwoArraysModel
	{
		public string[] First { get; set; } = [];
		public string[] Second { get; set; } = [];
	}

	private sealed class NumbersModel
	{
		public List<int> Numbers { get; set; } = [];
	}

	private sealed class CountingNumbersModel
	{
		[ThreadStatic]
		public static int Constructed;

		public CountingNumbersModel() => Constructed++;

		public List<int> Numbers { get; set; } = [];
		public string Name { get; set; } = string.Empty;
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

	private sealed class TwoOwnersModel
	{
		public Owner? Owner { get; set; }
		public Owner? Other { get; set; }
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

	private sealed class CountingOwnerStringModel
	{
		public CountingOwner? Owner { get; set; }
		public string? Name { get; set; }
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

	private sealed class EveryKindModel
	{
		public Level Level { get; set; }
		public bool Flag { get; set; }
		public DateTime Created { get; set; }
		public DateTimeOffset Seen { get; set; }
		public Guid Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public int Small { get; set; }
		public long Big { get; set; }
		public double Ratio { get; set; }
		public float Weight { get; set; }
		public decimal Amount { get; set; }
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

	private sealed class RequiredNameLevelModel
	{
		public Level Level { get; set; }
		public required string Name { get; set; }
	}

	private sealed class NonNullableNameModel
	{
		public string Name { get; set; } = string.Empty;
		public int Count { get; set; }
	}

	private sealed class NullableCountModel
	{
		public int? Count { get; set; }
	}

	private sealed class CountingNameModel
	{
		[ThreadStatic]
		public static int Constructed;

		public CountingNameModel() => Constructed++;

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

	private sealed class PlainLevelModel
	{
		[JsonConverter(typeof(JsonStringEnumConverter))]
		public Level Plain { get; set; }
	}

	private sealed class DeferredListModel
	{
		[JsonConverter(typeof(DeferringConverterFactory))]
		public List<int> Numbers { get; set; } = [];
	}

	private sealed class CustomListModel
	{
		[JsonConverter(typeof(ArrayOnlyIntListConverter))]
		public List<int> Numbers { get; set; } = [];
	}

	private sealed class NullableTagsModel
	{
		public string Name { get; set; } = string.Empty;

		[JsonConverter(typeof(WrappingTagsFactory))]
		public ImmutableArray<int>? Tags { get; set; }
	}

	private sealed class OwnTagsModel
	{
		public string Name { get; set; } = string.Empty;

		[JsonConverter(typeof(OwnWrapperFactory))]
		public ImmutableArray<int>? Tags { get; set; }
	}

	private sealed class ObjectFactoryDictionaryModel
	{
		[JsonConverter(typeof(ObjectInfoDictionaryFactory))]
		public Dictionary<string, int>? Values { get; set; }
	}

	private sealed class ObjectWrapperTagsModel
	{
		[JsonConverter(typeof(ObjectInfoNullableArrayFactory))]
		public ImmutableArray<int>? Tags { get; set; }
	}

	private sealed class ConstructedPairModel
	{
		[JsonConverter(typeof(DefaultObjectNullablePairFactory))]
		public ConstructedPair? Value { get; set; }
	}

	private sealed class SuppliedPairModel
	{
		public ConstructedPair Value { get; set; }
	}

	[method: JsonConstructor]
	private readonly struct ConstructedPair(int x, int y)
	{
		public int X { get; } = x;
		public int Y { get; } = y;
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

	private sealed class DoublingLevelConverter : JsonConverter<Level>
	{
		public override Level Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			(Level)(reader.GetInt32() * 2);

		public override void Write(Utf8JsonWriter writer, Level value, JsonSerializerOptions options) =>
			writer.WriteNumberValue((int)value);
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

	private sealed class UpperCaseStringConverter : JsonConverter<string>
	{
		public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			reader.GetString()!.ToUpperInvariant();

		public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value);
	}

	private sealed class ReversingStringListConverter : JsonConverter<List<string>>
	{
		public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var values = JsonSerializer.Deserialize<List<string>>(ref reader, options)!;
			values.Reverse();
			return values;
		}

		public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options) =>
			JsonSerializer.Serialize(writer, value, options);
	}

	private sealed class RejectingNegativeConverter : JsonConverter<ImmutableArray<int>>
	{
		public override ImmutableArray<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var values = JsonSerializer.Deserialize<List<int>>(ref reader, options) ?? [];
			if (values.Exists(value => value < 0))
				throw new JsonException("A tag must not be negative.");

			return [.. values];
		}

		public override void Write(Utf8JsonWriter writer, ImmutableArray<int> value, JsonSerializerOptions options) =>
			JsonSerializer.Serialize(writer, value.ToArray(), options);
	}

	private sealed class WrappingTagsFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ImmutableArray<int>?);

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.GetNullableConverter<ImmutableArray<int>>(
				JsonMetadataServices.CreateValueInfo<ImmutableArray<int>>(options, new RejectingNegativeConverter())
			);
	}

	private sealed class NullableDoublingLevelFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Level?);

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.GetNullableConverter<Level>(JsonMetadataServices.CreateValueInfo<Level>(options, new DoublingLevelConverter()));
	}

	private sealed class NullableDoublingIntFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int?);

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.GetNullableConverter<int>(JsonMetadataServices.CreateValueInfo<int>(options, new DoublingIntConverter()));
	}

	// Answers true for int? on its first call only, so the serializer resolves the contract on an answer a later call does not repeat.
	private sealed class FlippingNullableDoublingIntFactory : JsonConverterFactory
	{
		private int _calls;

		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int?) && Interlocked.Increment(ref _calls) == 1;

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.GetNullableConverter<int>(JsonMetadataServices.CreateValueInfo<int>(options, new DoublingIntConverter()));
	}

	private sealed class OwnWrapperFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ImmutableArray<int>?);

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.GetNullableConverter<ImmutableArray<int>>(options);
	}

	private sealed class ObjectInfoDictionaryFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Dictionary<string, int>);

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.CreateObjectInfo<Dictionary<string, int>>(
				options,
				new JsonObjectInfoValues<Dictionary<string, int>> { ObjectCreator = static () => [] }
			).Converter;
	}

	private sealed class ObjectInfoStringFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(string);

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.CreateObjectInfo<string>(options, new JsonObjectInfoValues<string>()).Converter;
	}

	private sealed class ObjectInfoNullableArrayFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ImmutableArray<int>?);

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.GetNullableConverter<ImmutableArray<int>>(
				JsonMetadataServices.CreateObjectInfo<ImmutableArray<int>>(
					options,
					new JsonObjectInfoValues<ImmutableArray<int>> { ObjectCreator = static () => [] }
				)
			);
	}

	private sealed class DefaultObjectNullablePairFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ConstructedPair?);

		public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
			JsonMetadataServices.GetNullableConverter<ConstructedPair>(
				JsonMetadataServices.CreateObjectInfo<ConstructedPair>(
					options,
					new JsonObjectInfoValues<ConstructedPair> { ObjectCreator = static () => default }
				)
			);
	}

	// Resolves a contract of its own for the pair and hands the model's property a stricter one that requires x, so only
	// the serializer, which reads through the property's metadata, rejects a pair without x.
	private sealed class SuppliedPairResolver : IJsonTypeInfoResolver
	{
		private readonly DefaultJsonTypeInfoResolver _inner = new();

		public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
		{
			if (type == typeof(SuppliedPairModel))
				return CreateModelInfo(options);

			return type == typeof(ConstructedPair) ? CreatePairInfo(options, requireX: false) : _inner.GetTypeInfo(type, options);
		}

		private static JsonTypeInfo<SuppliedPairModel> CreateModelInfo(JsonSerializerOptions options)
		{
			var modelInfo = JsonMetadataServices.CreateObjectInfo<SuppliedPairModel>(
				options,
				new JsonObjectInfoValues<SuppliedPairModel>
				{
					ObjectCreator = static () => new SuppliedPairModel(),
					PropertyMetadataInitializer = _ => [CreateValueProperty(options)]
				}
			);

			// The metadata API marks the type strict for numbers, which would keep the type off the direct path. A
			// generated context leaves the type's number handling unset, as here.
			modelInfo.NumberHandling = null;
			return modelInfo;
		}

		private static JsonPropertyInfo CreateValueProperty(JsonSerializerOptions options) =>
			JsonMetadataServices.CreatePropertyInfo<ConstructedPair>(
				options,
				new JsonPropertyInfoValues<ConstructedPair>
				{
					IsProperty = true,
					IsPublic = true,
					DeclaringType = typeof(SuppliedPairModel),
					PropertyName = nameof(SuppliedPairModel.Value),
					JsonPropertyName = "value",
					PropertyTypeInfo = CreatePairInfo(options, requireX: true),
					Converter = (JsonConverter<ConstructedPair>)options.GetConverter(typeof(ConstructedPair)),
					Getter = static model => ((SuppliedPairModel)model).Value,
					Setter = static (model, value) => ((SuppliedPairModel)model).Value = value
				}
			);

		private static JsonTypeInfo<ConstructedPair> CreatePairInfo(JsonSerializerOptions options, bool requireX) =>
			JsonMetadataServices.CreateObjectInfo<ConstructedPair>(
				options,
				new JsonObjectInfoValues<ConstructedPair>
				{
					ObjectCreator = static () => default,
					PropertyMetadataInitializer = _ => [CreatePairXProperty(options, isRequired: requireX)]
				}
			);

		// The serializer sets a struct's members on a boxed copy, so the setter replaces the pair inside that box.
		private static JsonPropertyInfo CreatePairXProperty(JsonSerializerOptions options, bool isRequired)
		{
			var property = JsonMetadataServices.CreatePropertyInfo<int>(
				options,
				new JsonPropertyInfoValues<int>
				{
					IsProperty = true,
					IsPublic = true,
					DeclaringType = typeof(ConstructedPair),
					PropertyName = nameof(ConstructedPair.X),
					JsonPropertyName = "x",
					Getter = static pair => ((ConstructedPair)pair).X,
					Setter = static (pair, x) => Unsafe.Unbox<ConstructedPair>(pair) = new ConstructedPair(x, ((ConstructedPair)pair).Y)
				}
			);
			property.IsRequired = isRequired;
			return property;
		}
	}

	private sealed class PreservingReferenceHandler : ReferenceHandler
	{
		public override ReferenceResolver CreateResolver() => ReferenceHandler.Preserve.CreateResolver();
	}
}

internal sealed class TranslatedListModel
{
	public string Name { get; set; } = string.Empty;

	[JsonConverter(typeof(DeferringConverterFactory))]
	public List<int> Numbers { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TranslatedListModel))]
internal sealed partial class TranslatedListContext : JsonSerializerContext;

internal sealed class CountModel
{
	public int Count { get; set; }
}

// Carries the serializable attribute without the generator, which the compiler reports and the generator skips.
[JsonSerializable(typeof(CountModel))]
internal sealed class AttributedHandWrittenContext() : JsonSerializerContext(null), IJsonTypeInfoResolver
{
	protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

	public override JsonTypeInfo? GetTypeInfo(Type type) => null;

	JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options) =>
		type == typeof(CountModel) ? DirectBindingConverterCellTests.CreateDoublingCountInfo(options) : null;
}
