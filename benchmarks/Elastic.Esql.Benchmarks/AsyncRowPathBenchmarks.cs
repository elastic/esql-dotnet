// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Benchmarks;

[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class AsyncRowPathBenchmarks
{
	private const int FlatRowCount = 100;
	private const int WideRowCount = 1000;

	private byte[] _flatPayload = null!;
	private byte[] _widePayload = null!;
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

		_flatPayload = BuildFlatPayload();
		_widePayload = BuildWidePayload();
	}

	[Benchmark]
	public async Task<int> Flat_Stream()
	{
		using var stream = new MemoryStream(_flatPayload, writable: false);
		var count = 0;

		await foreach (var _ in _reader.ReadRowsAsync<FlatDocument>(stream))
			count++;

		return count;
	}

	[Benchmark]
	public async Task<int> Flat_PipeReader()
	{
		using var stream = new MemoryStream(_flatPayload, writable: false);
		var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: false));
		var count = 0;

		try
		{
			await foreach (var _ in _reader.ReadRowsAsync<FlatDocument>(pipeReader))
				count++;
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}

		return count;
	}

	[Benchmark]
	public async Task<int> Wide_Stream()
	{
		using var stream = new MemoryStream(_widePayload, writable: false);
		var count = 0;

		await foreach (var _ in _reader.ReadRowsAsync<WideDocument>(stream))
			count++;

		return count;
	}

	[Benchmark]
	public async Task<int> Wide_PipeReader()
	{
		using var stream = new MemoryStream(_widePayload, writable: false);
		var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: false));
		var count = 0;

		try
		{
			await foreach (var _ in _reader.ReadRowsAsync<WideDocument>(pipeReader))
				count++;
		}
		finally
		{
			await pipeReader.CompleteAsync().ConfigureAwait(false);
		}

		return count;
	}

	private static byte[] BuildFlatPayload() =>
		BuildEsqlPayload(
			["name:keyword", "count:integer", "score:double", "active:boolean", "category:keyword"],
			FlatRowCount,
			(w, i) =>
			{
				w.WriteStringValue($"item-{i}");
				w.WriteNumberValue(i);
				w.WriteNumberValue(i * 1.5);
				w.WriteBooleanValue(true);
				w.WriteStringValue($"cat-{i % 5}");
			}
		);

	private static byte[] BuildWidePayload() =>
		BuildEsqlPayload(
			[
				"field1:keyword", "field2:integer", "field3:double", "field4:boolean", "field5:keyword",
				"field6:integer", "field7:double", "field8:boolean", "field9:keyword", "field10:integer"
			],
			WideRowCount,
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

	private static byte[] BuildEsqlPayload(string[] columnDefs, int rowCount, Action<Utf8JsonWriter, int> writeRow)
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
		for (var i = 0; i < rowCount; i++)
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
}
