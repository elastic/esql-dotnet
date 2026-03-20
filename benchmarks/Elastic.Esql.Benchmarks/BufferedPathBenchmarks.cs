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
public class BufferedPathBenchmarks
{
	private const int RowCount = 100;

	private byte[] _streamingPayload = null!;
	private byte[] _valuesFirstPayload = null!;
	private byte[] _requireIdPayload = null!;
	private EsqlResponseReader _reader = null!;

	[GlobalSetup]
	public void Setup()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = BenchmarkJsonContext.Default
		};
		var metadata = new JsonMetadataManager(options);
		_reader = new EsqlResponseReader(metadata);

		_streamingPayload = BuildPayload(columnsFirst: true, includeIdAfterValues: false);
		_valuesFirstPayload = BuildPayload(columnsFirst: false, includeIdAfterValues: false);
		_requireIdPayload = BuildPayload(columnsFirst: true, includeIdAfterValues: true);
	}

	[Benchmark(Baseline = true)]
	public List<FlatDocument> Flat_Streaming()
	{
		using var stream = new MemoryStream(_streamingPayload, writable: false);
		using var result = _reader.ReadRows<FlatDocument>(stream);
		return result.Rows.ToList();
	}

	[Benchmark]
	public List<FlatDocument> Flat_Buffered_ValuesFirst()
	{
		using var stream = new MemoryStream(_valuesFirstPayload, writable: false);
		using var result = _reader.ReadRows<FlatDocument>(stream);
		return result.Rows.ToList();
	}

	[Benchmark]
	public List<FlatDocument> Flat_Buffered_RequireId()
	{
		using var stream = new MemoryStream(_requireIdPayload, writable: false);
		using var result = _reader.ReadRows<FlatDocument>(stream, requireId: true);
		return result.Rows.ToList();
	}

	private static byte[] BuildPayload(bool columnsFirst, bool includeIdAfterValues)
	{
		var buffer = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(buffer);

		writer.WriteStartObject();

		if (columnsFirst)
		{
			WriteColumns(writer);
			WriteValues(writer);
			if (includeIdAfterValues)
				writer.WriteString("id", "bench-query-1");
		}
		else
		{
			WriteValues(writer);
			WriteColumns(writer);
		}

		writer.WriteEndObject();
		writer.Flush();

		return buffer.WrittenSpan.ToArray();
	}

	private static void WriteColumns(Utf8JsonWriter writer)
	{
		writer.WritePropertyName("columns");
		writer.WriteStartArray();
		WriteColumn(writer, "name", "keyword");
		WriteColumn(writer, "count", "integer");
		WriteColumn(writer, "score", "double");
		WriteColumn(writer, "active", "boolean");
		WriteColumn(writer, "category", "keyword");
		writer.WriteEndArray();
	}

	private static void WriteColumn(Utf8JsonWriter writer, string name, string type)
	{
		writer.WriteStartObject();
		writer.WriteString("name", name);
		writer.WriteString("type", type);
		writer.WriteEndObject();
	}

	private static void WriteValues(Utf8JsonWriter writer)
	{
		writer.WritePropertyName("values");
		writer.WriteStartArray();
		for (var i = 0; i < RowCount; i++)
		{
			writer.WriteStartArray();
			writer.WriteStringValue($"item-{i}");
			writer.WriteNumberValue(i);
			writer.WriteNumberValue(i * 1.5);
			writer.WriteBooleanValue(true);
			writer.WriteStringValue($"cat-{i % 5}");
			writer.WriteEndArray();
		}
		writer.WriteEndArray();
	}
}
