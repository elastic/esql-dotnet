// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;
using Elastic.Esql.Tests.Execution;

namespace Elastic.Esql.Tests.Materialization;

public class DirectBindingTypedSetterTests
{
	private const string DerivedJson = """
		{
		  "columns": [
		    { "name": "name", "type": "keyword" },
		    { "name": "count", "type": "integer" },
		    { "name": "when", "type": "date" }
		  ],
		  "values": [
		    ["a", 1, "2024-01-02T03:04:05.000Z"],
		    ["b", null, null]
		  ]
		}
		""";

	private const string StructJson = """{"columns":[{"name":"x","type":"integer"},{"name":"y","type":"keyword"}],"values":[[7,"s"]]}""";

	private const string PaddedNameJson = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["  a  ",1]]}""";

	[Test]
	public void Build_ReferenceModel_CompilesTypedSetterForEveryColumn()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var layout = BuildLayout<TypedSetterDerivedModel>(("name", "keyword"), ("count", "integer"), ("when", "date"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.TypedSetters.Should().HaveCount(3);
		layout.DirectBinder.TypedSetters.Should().AllSatisfy(setter => setter.Should().NotBeNull());
		layout.DirectBinder.TypedSetters[1].Should().BeOfType<Action<object, int>>();
	}

	[Test]
	public void ReadRows_BaseClassAndNullableProperties_Bind()
	{
		var rows = ReadRows<TypedSetterDerivedModel>(DerivedJson);

		rows.Should().HaveCount(2);
		rows[0].Name.Should().Be("a");
		rows[0].Count.Should().Be(1);
		rows[0].When.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
		rows[1].Name.Should().Be("b");
		rows[1].Count.Should().BeNull();
		rows[1].When.Should().BeNull();
	}

	[Test]
	public void Build_StructModel_KeepsBoxingSetters()
	{
		var layout = BuildLayout<TypedSetterStructModel>(("x", "integer"), ("y", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.TypedSetters.Should().HaveCount(2);
		layout.DirectBinder.TypedSetters.Should().AllSatisfy(setter => setter.Should().BeNull());
	}

	[Test]
	public void ReadRows_StructModel_Binds()
	{
		var rows = ReadRows<TypedSetterStructModel>(StructJson);

		rows.Should().ContainSingle();
		rows[0].X.Should().Be(7);
		rows[0].Y.Should().Be("s");
	}

	[Test]
	public void Build_InitOnlyModelUnderReflection_CompilesTypedSetters()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
		var layout = BuildLayout<InitOnlyModel>(options, ("name", "keyword"), ("count", "integer"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.TypedSetters.Should().AllSatisfy(setter => setter.Should().NotBeNull());
	}

	[Test]
	public void ReadRows_InitOnlyModelUnderReflection_Binds()
	{
		var json = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["a",1]]}""";
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

		var rows = ReadRows<InitOnlyModel>(json, options);

		rows.Should().ContainSingle();
		rows[0].Name.Should().Be("a");
		rows[0].Count.Should().Be(1);
	}

	[Test]
	public void ReadRows_ModifierReplacesSetter_IsHonored()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { TrimNameOnSet } }
		};

		var rows = ReadRows<TrimmedNameModel>(PaddedNameJson, options);

		rows.Should().ContainSingle().Which.Name.Should().Be("a");
	}

	[Test]
	public void ReadRows_ModifierAddedAfterConstruction_IsHonored()
	{
		var resolver = new DefaultJsonTypeInfoResolver();
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
		var reader = new EsqlResponseReader(new JsonMetadataManager(options));
		resolver.Modifiers.Add(TrimNameOnSet);
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(PaddedNameJson));
		using var results = reader.ReadRows<TrimmedNameModel>(stream);

		results.Rows.ToList().Should().ContainSingle().Which.Name.Should().Be("a");
	}

	[Test]
	public void ReadRows_ModifierAddedToUnusedChainPartAfterFirstRead_IsHonored()
	{
		var tail = new DefaultJsonTypeInfoResolver();
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(MaterializationTestJsonContext.Default, tail)
		};
		var reader = new EsqlResponseReader(new JsonMetadataManager(options));
		using var firstStream = new MemoryStream(Encoding.UTF8.GetBytes(DerivedJson));
		using var firstResults = reader.ReadRows<TypedSetterDerivedModel>(firstStream);
		_ = firstResults.Rows.ToList();
		tail.Modifiers.Add(TrimNameOnSet);
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(PaddedNameJson));
		using var results = reader.ReadRows<TrimmedNameModel>(stream);

		results.Rows.ToList().Should().ContainSingle().Which.Name.Should().Be("a");
	}

	[Test]
	public void Build_ContextConstructedProvider_CompilesTypedSetters()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var provider = new EsqlQueryProvider(MaterializationTestJsonContext.Default, new CapturingQueryExecutor());
		var layout = ColumnLayout.Build(Columns(("Name", "keyword"), ("Count", "integer")), typeof(TypedSetterDerivedModel), provider.Metadata);

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.TypedSetters.Should().AllSatisfy(setter => setter.Should().NotBeNull());
	}

	[Test]
	public void ReadRows_CombinedResolverWithModifierInSecondPart_IsHonored()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				new DefaultJsonTypeInfoResolver { Modifiers = { TrimNameOnSet } }
			)
		};

		var rows = ReadRows<TrimmedNameModel>(PaddedNameJson, options);

		rows.Should().ContainSingle().Which.Name.Should().Be("a");
	}

	[Test]
	public void Build_CombinedResolverWithCustomPart_KeepsBoxingSetters()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(new PassThroughResolver(), new DefaultJsonTypeInfoResolver())
		};

		var layout = BuildLayout<TrimmedNameModel>(options, ("name", "keyword"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.TypedSetters.Should().AllSatisfy(setter => setter.Should().BeNull());
	}

	[Test]
	public void ReadRows_CombinedHandWrittenContextWithReplacedSetter_IsHonored()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(new TrimmingContext(), new DefaultJsonTypeInfoResolver())
		};

		var rows = ReadRows<TrimmedNameModel>(PaddedNameJson, options);

		rows.Should().ContainSingle().Which.Name.Should().Be("a");
	}

	[Test]
	public void ReadRows_DefaultResolverSubclassReplacingSetter_IsHonored()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new TrimmingResolver() };

		var rows = ReadRows<TrimmedNameModel>(PaddedNameJson, options);

		rows.Should().ContainSingle().Which.Name.Should().Be("a");
	}

	private static void TrimNameOnSet(JsonTypeInfo typeInfo)
	{
		if (typeInfo.Type != typeof(TrimmedNameModel))
			return;

		foreach (var property in typeInfo.Properties)
		{
			if (string.Equals(property.Name, "name", StringComparison.Ordinal))
				property.Set = static (instance, value) => ((TrimmedNameModel)instance).Name = ((string?)value)?.Trim() ?? string.Empty;
		}
	}

	private static ColumnLayout BuildLayout<T>(params (string Name, string Type)[] columns) =>
		BuildLayout<T>(null, columns);

	private static ColumnLayout BuildLayout<T>(JsonSerializerOptions? options, params (string Name, string Type)[] columns) =>
		ColumnLayout.Build(Columns(columns), typeof(T), CreateMetadata(options));

	private static List<T> ReadRows<T>(string json, JsonSerializerOptions? options = null)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var results = new EsqlResponseReader(CreateMetadata(options)).ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	private static EsqlResponseReader.ColumnInfo[] Columns(params (string Name, string Type)[] columns)
	{
		var columnInfos = new EsqlResponseReader.ColumnInfo[columns.Length];
		for (var i = 0; i < columns.Length; i++)
			columnInfos[i] = new EsqlResponseReader.ColumnInfo(columns[i].Name, columns[i].Type);

		return columnInfos;
	}

	private static JsonMetadataManager CreateMetadata(JsonSerializerOptions? options) => new(options ?? CreateOptions());

	private static JsonSerializerOptions CreateOptions() =>
		new(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(MaterializationTestJsonContext.Default, EsqlTestMappingContext.Default)
		};

	private sealed class InitOnlyModel
	{
		public string Name { get; init; } = string.Empty;
		public int Count { get; init; }
	}

	private sealed class TrimmedNameModel
	{
		public string Name { get; set; } = string.Empty;
		public int Count { get; set; }
	}

	private sealed class PassThroughResolver : IJsonTypeInfoResolver
	{
		private readonly DefaultJsonTypeInfoResolver _inner = new();

		public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => _inner.GetTypeInfo(type, options);
	}

	private sealed class TrimmingResolver : DefaultJsonTypeInfoResolver
	{
		public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
		{
			var typeInfo = base.GetTypeInfo(type, options);
			TrimNameOnSet(typeInfo);
			return typeInfo;
		}
	}

	// Carries no [JsonSerializable], so nothing marks it as a source-generated context whose setters can be trusted. It
	// re-implements the resolver interface because the base implementation only serves the context's own options.
	private sealed class TrimmingContext() : JsonSerializerContext(null), IJsonTypeInfoResolver
	{
		private readonly DefaultJsonTypeInfoResolver _inner = new();

		protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

		public override JsonTypeInfo? GetTypeInfo(Type type) => ((IJsonTypeInfoResolver)this).GetTypeInfo(type, Options);

		JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
		{
			var typeInfo = _inner.GetTypeInfo(type, options);
			TrimNameOnSet(typeInfo);
			return typeInfo;
		}
	}
}
