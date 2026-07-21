// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class EsqlResponseReaderReadAheadTests
{
	private const string RowsJson =
		"""{"columns":[{"name":"value","type":"keyword"},{"name":"count","type":"integer"}],"values":[["first",1],["second",2]]}""";

	[Test]
	public void ReadRows_Stream_ResponseArrivesInFirstRead_YieldsRowsWithoutSecondRead()
	{
		using var stream = new SingleReadStream(Encoding.UTF8.GetBytes(RowsJson));
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[1].Value.Should().Be("second");
	}

	[Test]
	public async Task ReadRowsAsync_Stream_ResponseArrivesInFirstRead_YieldsRowsWithoutSecondRead()
	{
		using var stream = new SingleReadStream(Encoding.UTF8.GetBytes(RowsJson));
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[1].Value.Should().Be("second");
	}

	/// <summary>
	/// Returns the full payload on the first read and throws on any further read. A response that
	/// fits into the first chunk must stream all rows without another read: on a live connection
	/// that extra read blocks until more bytes arrive, delaying rows by one network round trip.
	/// The payload must fit into the reader's initial 16 KB buffer so a single read suffices.
	/// </summary>
	private sealed class SingleReadStream(byte[] data) : Stream
	{
		private bool _read;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => data.Length;

		public override long Position
		{
			get => 0;
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			ThrowIfAlreadyRead();
			data.AsSpan().CopyTo(buffer.AsSpan(offset));
			return data.Length;
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
			Task.FromResult(Read(buffer, offset, count));

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			ThrowIfAlreadyRead();
			data.AsSpan().CopyTo(buffer.Span);
			return ValueTask.FromResult(data.Length);
		}

		private void ThrowIfAlreadyRead()
		{
			if (_read)
				throw new InvalidOperationException("The response was fully delivered by the first read; no further stream reads are expected.");

			_read = true;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private static EsqlResponseReader CreateReader()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default
			)
		};

		var metadata = new JsonMetadataManager(options);
		return new EsqlResponseReader(metadata);
	}
}
