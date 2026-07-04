// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class BatchDeserializationTests
{
	// =========================================================================
	// Ordering and completeness across batch boundaries
	// =========================================================================

	[Test]
	public void ReadRows_NestedType_VariousRowCounts_AllRowsInOrder()
	{
		foreach (var rowCount in new[] { 1, 63, 64, 65, 128, 200 })
		{
			using var stream = CreateStream(BuildPayload(rowCount, DefaultRow));
			var reader = CreateReader();

			using var response = reader.ReadRows<BatchPerson>(stream);
			var results = response.Rows.ToList();

			AssertNestedRows(results, rowCount);
		}
	}

	[Test]
	public async Task ReadRowsAsync_NestedType_ManyRows_AllRowsInOrder()
	{
		using var stream = CreateStream(BuildPayload(200, DefaultRow));
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<BatchPerson>(stream);
		var results = new List<BatchPerson>();
		await foreach (var row in response.Rows)
			results.Add(row);

		AssertNestedRows(results, 200);
	}

	[Test]
	public void ReadRows_NestedType_LargeValues_AllRowsInOrder()
	{
		var street = new string('s', 2048);
		using var stream = CreateStream(BuildPayload(100, i => $"""["person-{i}", 20, "{street}", "city-0"]"""));
		var reader = CreateReader();

		using var response = reader.ReadRows<BatchPerson>(stream);
		var results = response.Rows.ToList();

		results.Should().HaveCount(100);
		for (var i = 0; i < 100; i++)
		{
			results[i].Name.Should().Be($"person-{i}");
			results[i].Address.Should().NotBeNull();
			results[i].Address!.Street.Should().Be(street);
		}
	}

	// =========================================================================
	// TypeInfo resolution fallbacks (AOT path must never silently use reflection)
	// =========================================================================

	[Test]
	public void ReadRows_NestedType_ContextWithoutListTypeInfo_AllRowsInOrder()
	{
		using var stream = CreateStream(BuildPayload(100, DefaultRow));
		var reader = CreateReaderWithoutListTypeInfo();

		using var response = reader.ReadRows<BatchPerson>(stream);
		var results = response.Rows.ToList();

		AssertNestedRows(results, 100);
	}

	[Test]
	public void ReadRows_NestedType_ReflectionOptions_AllRowsInOrder()
	{
		using var stream = CreateStream(BuildPayload(100, DefaultRow));
		var reader = CreateReflectionReader();

		using var response = reader.ReadRows<BatchPerson>(stream);
		var results = response.Rows.ToList();

		AssertNestedRows(results, 100);
	}

	// =========================================================================
	// Exception behavior
	// =========================================================================

	[Test]
	public void ReadRows_NestedType_MalformedRowMidStream_ThrowsJsonException()
	{
		// Row 70 carries a JSON number where address.street (string) is expected;
		// the failure must surface as JsonException during enumeration.
		var payload = BuildPayload(100, i => i == 70
			? $"""["person-{i}", 20, 12345, "city-0"]"""
			: DefaultRow(i));

		var act = () =>
		{
			using var stream = CreateStream(payload);
			var reader = CreateReader();
			using var response = reader.ReadRows<BatchPerson>(stream);
			_ = response.Rows.ToList();
		};

		_ = act.Should().Throw<JsonException>();
	}

	// =========================================================================
	// Cancellation
	// =========================================================================

	[Test]
	public async Task ReadRowsAsync_NestedType_PreCanceledToken_ThrowsOperationCanceledException()
	{
		using var stream = CreateStream(BuildPayload(100, DefaultRow));
		var reader = CreateReader();
		var cancellationToken = new CancellationToken(canceled: true);

		var act = async () =>
		{
			await foreach (var _ in (await reader.ReadRowsAsync<BatchPerson>(stream, cancellationToken: cancellationToken)).Rows)
			{
			}
		};

		_ = await act.Should().ThrowAsync<OperationCanceledException>();
	}

	[Test]
	public async Task ReadRowsAsync_NestedType_CancelMidStream_ThrowsOperationCanceledException()
	{
		var payload = Encoding.UTF8.GetBytes(BuildPayload(2000, DefaultRow));
		using var stream = new ThrottledReadStream(payload, chunkSize: 1024);
		var reader = CreateReader();
		using var cts = new CancellationTokenSource();

		var act = async () =>
		{
			var consumed = 0;
			await foreach (var _ in (await reader.ReadRowsAsync<BatchPerson>(stream, cancellationToken: cts.Token)).Rows)
			{
				consumed++;
				if (consumed == 100)
					cts.Cancel();
			}
		};

		_ = await act.Should().ThrowAsync<OperationCanceledException>();
	}

	// =========================================================================
	// Streaming semantics (bounded prefetch)
	// =========================================================================

	[Test]
	public void ReadRows_FlatType_FirstRowAvailable_BeforeStreamFullyConsumed()
	{
		// Flat layouts stream row-at-a-time: the first row must materialize after a
		// small prefix of the response. Rows are ~270 bytes, so 8192 bytes is a few
		// dozen rows out of 200 and far below any whole-response drain.
		var filler = new string('v', 256);
		var sb = new StringBuilder();
		sb.Append("""{ "columns": [ { "name": "value", "type": "keyword" }, { "name": "count", "type": "integer" } ], "values": [""");
		for (var i = 0; i < 200; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append($"""["{filler}", {i}]""");
		}
		sb.Append("] }");

		var payload = Encoding.UTF8.GetBytes(sb.ToString());
		using var stream = new ThrottledReadStream(payload, chunkSize: 1024);
		var reader = CreateFlatReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		using var enumerator = response.Rows.GetEnumerator();

		enumerator.MoveNext().Should().BeTrue();
		enumerator.Current.Value.Should().Be(filler);
		stream.TotalBytesRead.Should().BeLessThan(8192);
	}

	[Test]
	public void ReadRows_NestedType_FirstRowAvailable_BeforeStreamFullyConsumed()
	{
		// Nested layouts may prefetch up to one deserialization batch before the first
		// yield, but must never drain the whole response first.
		var payload = Encoding.UTF8.GetBytes(BuildPayload(2000, DefaultRow));
		using var stream = new ThrottledReadStream(payload, chunkSize: 1024);
		var reader = CreateReader();

		using var response = reader.ReadRows<BatchPerson>(stream);
		using var enumerator = response.Rows.GetEnumerator();

		enumerator.MoveNext().Should().BeTrue();
		enumerator.Current.Name.Should().Be("person-0");
		stream.TotalBytesRead.Should().BeLessThan(payload.Length);
	}

	// =========================================================================
	// Interaction with buffered and requireId paths
	// =========================================================================

	[Test]
	public void ReadRows_NestedType_ValuesFirst_AllRowsInOrder()
	{
		using var stream = CreateStream(BuildPayload(200, DefaultRow, valuesFirst: true));
		var reader = CreateReader();

		using var response = reader.ReadRows<BatchPerson>(stream);
		var results = response.Rows.ToList();

		AssertNestedRows(results, 200);
	}

	[Test]
	public void ReadRows_NestedType_RequireId_IdAfterValues_CapturesIdAndRows()
	{
		using var stream = CreateStream(BuildPayload(200, DefaultRow, trailingId: "query-batch"));
		var reader = CreateReader();

		using var response = reader.ReadRows<BatchPerson>(stream, requireId: true);
		var results = response.Rows.ToList();

		AssertNestedRows(results, 200);
		response.Id.Should().Be("query-batch");
	}

	// =========================================================================
	// Helpers
	// =========================================================================

	private const string ColumnsJson =
		"""
		"columns": [
			{ "name": "name", "type": "keyword" },
			{ "name": "age", "type": "integer" },
			{ "name": "address.street", "type": "keyword" },
			{ "name": "address.city", "type": "keyword" }
		]
		""";

	private static string DefaultRow(int i) =>
		$"""["person-{i}", {20 + (i % 50)}, "street-{i}", "city-{i % 10}"]""";

	private static string BuildPayload(int rowCount, Func<int, string> rowJson, bool valuesFirst = false, string? trailingId = null)
	{
		var sb = new StringBuilder();
		sb.Append('{');

		if (!valuesFirst)
			sb.Append(ColumnsJson).Append(',');

		sb.Append("\"values\": [");
		for (var i = 0; i < rowCount; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append(rowJson(i));
		}
		sb.Append(']');

		if (valuesFirst)
			sb.Append(',').Append(ColumnsJson);

		if (trailingId is not null)
			sb.Append(", \"id\": \"").Append(trailingId).Append('"');

		sb.Append('}');
		return sb.ToString();
	}

	private static void AssertNestedRows(List<BatchPerson> results, int rowCount)
	{
		results.Should().HaveCount(rowCount);
		for (var i = 0; i < rowCount; i++)
		{
			results[i].Name.Should().Be($"person-{i}");
			results[i].Age.Should().Be(20 + (i % 50));
			results[i].Address.Should().NotBeNull();
			results[i].Address!.Street.Should().Be($"street-{i}");
			results[i].Address!.City.Should().Be($"city-{i % 10}");
		}
	}

	private static EsqlResponseReader CreateReader() =>
		new(new JsonMetadataManager(new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = BatchTestJsonContext.Default
		}));

	private static EsqlResponseReader CreateReaderWithoutListTypeInfo() =>
		new(new JsonMetadataManager(new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = BatchTestNoListJsonContext.Default
		}));

	private static EsqlResponseReader CreateReflectionReader() =>
		new(new JsonMetadataManager(new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = new DefaultJsonTypeInfoResolver()
		}));

	private static EsqlResponseReader CreateFlatReader() =>
		new(new JsonMetadataManager(new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = MaterializationTestJsonContext.Default
		}));

	private static MemoryStream CreateStream(string json) => new(Encoding.UTF8.GetBytes(json));

	/// <summary>Serves at most <c>chunkSize</c> bytes per read so streaming consumption is observable.</summary>
	private sealed class ThrottledReadStream(byte[] data, int chunkSize) : Stream
	{
		private int _position;

		public int TotalBytesRead { get; private set; }

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => data.Length;

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			var toCopy = Math.Min(chunkSize, Math.Min(count, data.Length - _position));
			Array.Copy(data, _position, buffer, offset, toCopy);
			_position += toCopy;
			TotalBytesRead += toCopy;
			return toCopy;
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(Read(buffer, offset, count));
		}

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var temp = new byte[Math.Min(chunkSize, buffer.Length)];
			var read = Read(temp, 0, temp.Length);
			temp.AsSpan(0, read).CopyTo(buffer.Span);
			return new ValueTask<int>(read);
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}

[JsonSerializable(typeof(BatchPerson))]
[JsonSerializable(typeof(List<BatchPerson>))]
public sealed partial class BatchTestJsonContext : JsonSerializerContext;

/// <summary>Registers the entity type but not <c>List&lt;BatchPerson&gt;</c> so list metadata is unresolvable.</summary>
[JsonSerializable(typeof(BatchPerson))]
public sealed partial class BatchTestNoListJsonContext : JsonSerializerContext;

public class BatchAddress
{
	public string Street { get; set; } = string.Empty;
	public string City { get; set; } = string.Empty;
}

public class BatchPerson
{
	public string Name { get; set; } = string.Empty;
	public int Age { get; set; }
	public BatchAddress? Address { get; set; }
}
