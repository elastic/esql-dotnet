// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class DictionaryColumnTests
{
	private sealed class TaggedModel
	{
		public string? Name { get; set; }
		public Dictionary<string, string>? Tags { get; set; }
	}

	[Test]
	public void ReadRows_DictionaryColumn_MaterializesAsObject()
	{
		const string json =
			"""{"columns":[{"name":"name","type":"keyword"},{"name":"tags","type":"object"}],"values":[["a",{"env":"prod","zone":"eu"}]]}""";

		var reader = CreateReflectionReader();
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

		var rows = reader.ReadRows<TaggedModel>(stream).Rows.ToList();

		_ = rows.Should().HaveCount(1);
		_ = rows[0].Tags.Should().ContainKey("env").WhoseValue.Should().Be("prod");
	}

	private static EsqlResponseReader CreateReflectionReader() =>
		new(new JsonMetadataManager(new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = new DefaultJsonTypeInfoResolver()
		}));
}
