// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Benchmarks;

[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class RowReaderBenchmarks
{
	private const int RowCount = 100;

	private byte[] _esqlFlatPayload = null!;
	private byte[] _rawJsonFlatPayload = null!;
	private byte[] _esqlNestedPayload = null!;
	private byte[] _rawJsonNestedPayload = null!;
	private byte[] _esqlDeepPayload = null!;
	private byte[] _rawJsonDeepPayload = null!;
	private byte[] _esqlWidePayload = null!;
	private byte[] _rawJsonWidePayload = null!;
	private byte[] _esqlMixedPayload = null!;
	private byte[] _rawJsonMixedPayload = null!;

	private EsqlResponseReader _reader = null!;
	private JsonSerializerOptions _options = null!;

	[GlobalSetup]
	public void Setup()
	{
		_options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = BenchmarkJsonContext.Default
		};
		var metadata = new JsonMetadataManager(_options);
		_reader = new EsqlResponseReader(metadata);

		_esqlFlatPayload = BuildEsqlFlatPayload();
		_rawJsonFlatPayload = BuildRawJsonFlatPayload();

		_esqlNestedPayload = BuildEsqlNestedPayload();
		_rawJsonNestedPayload = BuildRawJsonNestedPayload();

		_esqlDeepPayload = BuildEsqlDeepPayload();
		_rawJsonDeepPayload = BuildRawJsonDeepPayload();

		_esqlWidePayload = BuildEsqlWidePayload();
		_rawJsonWidePayload = BuildRawJsonWidePayload();

		_esqlMixedPayload = BuildEsqlMixedPayload();
		_rawJsonMixedPayload = BuildRawJsonMixedPayload();
	}

	// =========================================================================
	// Flat (5 scalar columns)
	// =========================================================================

	[Benchmark]
	public List<FlatDocument> Esql_Flat()
	{
		using var stream = new MemoryStream(_esqlFlatPayload, writable: false);
		return _reader.ReadRows<FlatDocument>(stream).ToList();
	}

	[Benchmark(Baseline = true)]
	public List<FlatDocument> RawJson_Flat() =>
		JsonSerializer.Deserialize<List<FlatDocument>>(_rawJsonFlatPayload, _options)!;

	// =========================================================================
	// Nested (1-level nested object)
	// =========================================================================

	[Benchmark]
	public List<NestedDocument> Esql_NestedOneLevel()
	{
		using var stream = new MemoryStream(_esqlNestedPayload, writable: false);
		return _reader.ReadRows<NestedDocument>(stream).ToList();
	}

	[Benchmark]
	public List<NestedDocument> RawJson_NestedOneLevel() =>
		JsonSerializer.Deserialize<List<NestedDocument>>(_rawJsonNestedPayload, _options)!;

	// =========================================================================
	// Deep (3-level nested object)
	// =========================================================================

	[Benchmark]
	public List<DeepDocument> Esql_DeepThreeLevels()
	{
		using var stream = new MemoryStream(_esqlDeepPayload, writable: false);
		return _reader.ReadRows<DeepDocument>(stream).ToList();
	}

	[Benchmark]
	public List<DeepDocument> RawJson_DeepThreeLevels() =>
		JsonSerializer.Deserialize<List<DeepDocument>>(_rawJsonDeepPayload, _options)!;

	// =========================================================================
	// Wide (10 flat scalar columns)
	// =========================================================================

	[Benchmark]
	public List<WideDocument> Esql_WideFlat()
	{
		using var stream = new MemoryStream(_esqlWidePayload, writable: false);
		return _reader.ReadRows<WideDocument>(stream).ToList();
	}

	[Benchmark]
	public List<WideDocument> RawJson_WideFlat() =>
		JsonSerializer.Deserialize<List<WideDocument>>(_rawJsonWidePayload, _options)!;

	// =========================================================================
	// Mixed (flat + multiple sibling nested objects)
	// =========================================================================

	[Benchmark]
	public List<MixedDocument> Esql_MixedFlatAndNested()
	{
		using var stream = new MemoryStream(_esqlMixedPayload, writable: false);
		return _reader.ReadRows<MixedDocument>(stream).ToList();
	}

	[Benchmark]
	public List<MixedDocument> RawJson_MixedFlatAndNested() =>
		JsonSerializer.Deserialize<List<MixedDocument>>(_rawJsonMixedPayload, _options)!;

	// =========================================================================
	// Payload generators — ES|QL format
	// =========================================================================

	private static byte[] BuildEsqlPayload(string[] columnDefs, Action<Utf8JsonWriter, int> writeRow)
	{
		var buffer = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(buffer);

		writer.WriteStartObject();
		writer.WritePropertyName("columns");
		writer.WriteStartArray();
		foreach (var col in columnDefs)
		{
			var parts = col.Split(':');
			writer.WriteStartObject();
			writer.WriteString("name", parts[0]);
			writer.WriteString("type", parts[1]);
			writer.WriteEndObject();
		}
		writer.WriteEndArray();

		writer.WritePropertyName("values");
		writer.WriteStartArray();
		for (var i = 0; i < RowCount; i++)
		{
			writer.WriteStartArray();
			writeRow(writer, i);
			writer.WriteEndArray();
		}
		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();

		return buffer.WrittenSpan.ToArray();
	}

	private static byte[] BuildRawJsonPayload<T>(Func<int, T> factory, JsonSerializerOptions options)
	{
		var items = new List<T>(RowCount);
		for (var i = 0; i < RowCount; i++)
			items.Add(factory(i));
		return JsonSerializer.SerializeToUtf8Bytes(items, options);
	}

	private static byte[] BuildEsqlFlatPayload() =>
		BuildEsqlPayload(
			["name:keyword", "count:integer", "score:double", "active:boolean", "category:keyword"],
			(w, i) =>
			{
				w.WriteStringValue($"item-{i}");
				w.WriteNumberValue(i);
				w.WriteNumberValue(i * 1.5);
				w.WriteBooleanValue(true);
				w.WriteStringValue($"cat-{i % 5}");
			}
		);

	private byte[] BuildRawJsonFlatPayload() =>
		BuildRawJsonPayload(i => new FlatDocument
		{
			Name = $"item-{i}",
			Count = i,
			Score = i * 1.5,
			Active = true,
			Category = $"cat-{i % 5}"
		}, _options);

	private static byte[] BuildEsqlNestedPayload() =>
		BuildEsqlPayload(
			["name:keyword", "age:integer", "address.street:keyword", "address.city:keyword"],
			(w, i) =>
			{
				w.WriteStringValue($"person-{i}");
				w.WriteNumberValue(20 + (i % 50));
				w.WriteStringValue($"street-{i}");
				w.WriteStringValue($"city-{i % 10}");
			}
		);

	private byte[] BuildRawJsonNestedPayload() =>
		BuildRawJsonPayload(i => new NestedDocument
		{
			Name = $"person-{i}",
			Age = 20 + (i % 50),
			Address = new NestedAddress { Street = $"street-{i}", City = $"city-{i % 10}" }
		}, _options);

	private static byte[] BuildEsqlDeepPayload() =>
		BuildEsqlPayload(
			["name:keyword", "level1.tag:keyword", "level1.child.label:keyword", "level1.child.inner.value:keyword"],
			(w, i) =>
			{
				w.WriteStringValue($"doc-{i}");
				w.WriteStringValue($"tag-{i}");
				w.WriteStringValue($"label-{i}");
				w.WriteStringValue($"val-{i}");
			}
		);

	private byte[] BuildRawJsonDeepPayload() =>
		BuildRawJsonPayload(i => new DeepDocument
		{
			Name = $"doc-{i}",
			Level1 = new DeepLevel1
			{
				Tag = $"tag-{i}",
				Child = new DeepLevel2
				{
					Label = $"label-{i}",
					Inner = new DeepLevel3 { Value = $"val-{i}" }
				}
			}
		}, _options);

	private static byte[] BuildEsqlWidePayload() =>
		BuildEsqlPayload(
			[
				"field1:keyword", "field2:integer", "field3:double", "field4:boolean", "field5:keyword",
				"field6:integer", "field7:double", "field8:boolean", "field9:keyword", "field10:integer"
			],
			(w, i) =>
			{
				w.WriteStringValue($"f1-{i}");
				w.WriteNumberValue(i);
				w.WriteNumberValue(i * 0.1);
				w.WriteBooleanValue(true);
				w.WriteStringValue($"f5-{i}");
				w.WriteNumberValue(i + 100);
				w.WriteNumberValue(i * 0.2);
				w.WriteBooleanValue(false);
				w.WriteStringValue($"f9-{i}");
				w.WriteNumberValue(i + 200);
			}
		);

	private byte[] BuildRawJsonWidePayload() =>
		BuildRawJsonPayload(i => new WideDocument
		{
			Field1 = $"f1-{i}",
			Field2 = i,
			Field3 = i * 0.1,
			Field4 = true,
			Field5 = $"f5-{i}",
			Field6 = i + 100,
			Field7 = i * 0.2,
			Field8 = false,
			Field9 = $"f9-{i}",
			Field10 = i + 200
		}, _options);

	private static byte[] BuildEsqlMixedPayload() =>
		BuildEsqlPayload(
			[
				"name:keyword", "age:integer", "address.street:keyword",
				"address.city:keyword", "contact.email:keyword", "contact.phone:keyword"
			],
			(w, i) =>
			{
				w.WriteStringValue($"person-{i}");
				w.WriteNumberValue(25 + (i % 40));
				w.WriteStringValue($"street-{i}");
				w.WriteStringValue($"city-{i % 10}");
				w.WriteStringValue($"user{i}@test.com");
				w.WriteStringValue($"555-{i:D4}");
			}
		);

	private byte[] BuildRawJsonMixedPayload() =>
		BuildRawJsonPayload(i => new MixedDocument
		{
			Name = $"person-{i}",
			Age = 25 + (i % 40),
			Address = new NestedAddress { Street = $"street-{i}", City = $"city-{i % 10}" },
			Contact = new MixedContact { Email = $"user{i}@test.com", Phone = $"555-{i:D4}" }
		}, _options);
}
