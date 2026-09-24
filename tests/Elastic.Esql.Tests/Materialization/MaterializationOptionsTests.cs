// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class MaterializationOptionsTests
{
	private const string IntsJson = /*lang=json,strict*/ """{"columns":[{"name":"n","type":"integer"}],"values":[[1],[2],[3]]}""";

	[Test]
	public void ReadRows_FirstUse_MakesOptionsReadOnly()
	{
		var options = CreateOptions();
		options.IsReadOnly.Should().BeFalse();

		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(IntsJson));
		using var results = CreateReader(options).ReadRows<int>(stream);
		var rows = results.Rows.ToList();

		options.IsReadOnly.Should().BeTrue();
		rows.Should().NotBeEmpty();
	}

	[Test]
	public void ReadScalar_WarmReader_AllocatesUnderOneKilobyte()
	{
		var options = CreateOptions();
		var reader = CreateReader(options);
		var payload = Encoding.UTF8.GetBytes(IntsJson);

		// Warm up: freeze options and fill the column-layout cache.
		using (var warmStream = new MemoryStream(payload))
			reader.ReadScalar<int>(warmStream);

		var before = GC.GetAllocatedBytesForCurrentThread();
		using var stream = new MemoryStream(payload);
		reader.ReadScalar<int>(stream);
		var after = GC.GetAllocatedBytesForCurrentThread();

		// Mutable options re-resolve a fresh JsonTypeInfo per GetTypeInfo call (~760 bytes each);
		// frozen options reuse the cache and cost near zero. This guards against the mutable-options trap.
		(after - before).Should().BeLessThan(1024);
	}

	private static EsqlResponseReader CreateReader(JsonSerializerOptions options) =>
		new(new JsonMetadataManager(options));

	private static JsonSerializerOptions CreateOptions() =>
		new(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default,
				new DefaultJsonTypeInfoResolver()
			)
		};
}
