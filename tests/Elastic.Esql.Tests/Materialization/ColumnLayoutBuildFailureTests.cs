// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class ColumnLayoutBuildFailureTests
{
	private sealed class ThrowingResolver : IJsonTypeInfoResolver
	{
		public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
			throw new FormatException("resolver bug");
	}

	/// <summary>Resolves entity types normally but throws for <c>List&lt;&gt;</c> requests, isolating list-metadata resolution.</summary>
	private sealed class SelectiveThrowingResolver : IJsonTypeInfoResolver
	{
		private readonly DefaultJsonTypeInfoResolver _inner = new();

		public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
			type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
				? throw new FormatException("resolver bug")
				: _inner.GetTypeInfo(type, options);
	}

	[Test]
	public void Build_ResolverThrowsUnexpectedException_Propagates()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new ThrowingResolver() };
		var metadata = new JsonMetadataManager(options);
		var columns = new EsqlResponseReader.ColumnInfo[] { new("value", "keyword") };

		var act = () => ColumnLayout.Build(columns, typeof(ScalarStringModel), metadata);

		_ = act.Should().Throw<FormatException>();
	}

	[Test]
	public void ReadRows_ResolverThrowsUnexpectedException_ListResolutionPropagates()
	{
		// A hand-written resolver leaves the nested layout without a binder, so streaming takes the batched path, which
		// resolves the List<T> metadata a flat schema never requests.
		const string json =
			"""{"columns":[{"name":"name","type":"keyword"},{"name":"address.street","type":"keyword"}],"values":[["John","123 Main St"]]}""";

		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new SelectiveThrowingResolver() };
		var reader = new EsqlResponseReader(new JsonMetadataManager(options));
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

		var act = () => reader.ReadRows<PersonModel>(stream).Rows.ToList();

		_ = act.Should().Throw<FormatException>();
	}
}
