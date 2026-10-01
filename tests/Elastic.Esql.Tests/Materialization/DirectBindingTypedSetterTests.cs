// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

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
	public void ReadRows_ForeignAttributeProvider_KeepsBoxingSetterAndBinds()
	{
		if (!RuntimeFeature.IsDynamicCodeSupported)
			return;

		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RedirectNameToForeignMember } }
		};

		var layout = BuildLayout<ForeignProviderModel>(options, ("name", "keyword"), ("count", "integer"));

		layout.DirectBinder.Should().NotBeNull();
		layout.DirectBinder.TypedSetters[0].Should().BeNull();
		layout.DirectBinder.TypedSetters[1].Should().NotBeNull();

		const string json = """{"columns":[{"name":"name","type":"keyword"},{"name":"count","type":"integer"}],"values":[["a",1]]}""";
		var rows = ReadRows<ForeignProviderModel>(json, options);

		rows.Should().ContainSingle();
		rows[0].Name.Should().Be("a");
		rows[0].Count.Should().Be(1);
	}

	// A contract modifier is the only way to reach the case the guard covers: an AttributeProvider naming a
	// member of a type the row instance is not.
	private static void RedirectNameToForeignMember(JsonTypeInfo typeInfo)
	{
		if (typeInfo.Type != typeof(ForeignProviderModel))
			return;

		foreach (var property in typeInfo.Properties)
		{
			if (string.Equals(property.Name, "name", StringComparison.Ordinal))
				property.AttributeProvider = typeof(UnrelatedNameModel).GetProperty(nameof(UnrelatedNameModel.Name));
		}
	}

	private static ColumnLayout BuildLayout<T>(params (string Name, string Type)[] columns) =>
		BuildLayout<T>(null, columns);

	private static ColumnLayout BuildLayout<T>(JsonSerializerOptions? options, params (string Name, string Type)[] columns)
	{
		var columnInfos = new EsqlResponseReader.ColumnInfo[columns.Length];
		for (var i = 0; i < columns.Length; i++)
			columnInfos[i] = new EsqlResponseReader.ColumnInfo(columns[i].Name, columns[i].Type);

		return ColumnLayout.Build(columnInfos, typeof(T), new JsonMetadataManager(options ?? CreateOptions()));
	}

	private static List<T> ReadRows<T>(string json, JsonSerializerOptions? options = null)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var results = new EsqlResponseReader(new JsonMetadataManager(options ?? CreateOptions())).ReadRows<T>(stream);
		return results.Rows.ToList();
	}

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

	private sealed class ForeignProviderModel
	{
		public string Name { get; set; } = string.Empty;
		public int Count { get; set; }
	}

	private sealed class UnrelatedNameModel
	{
		public string Name { get; set; } = string.Empty;
	}
}
