// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Execution;

public class ResponseReaderFailureTests
{
	private static EsqlResponseReader CreateReader() =>
		new(new JsonMetadataManager(new JsonSerializerOptions
		{
			TypeInfoResolver = EsqlTestMappingContext.Default,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		}));

	[Test]
	public async Task ReadRowsAsync_MalformedBody_ThrowsJsonException()
	{
		var reader = CreateReader();
		using var stream = new MemoryStream("""{"unexpected":true}"""u8.ToArray());

		var act = () => reader.ReadRowsAsync<LogEntry>(stream);

		await act.Should().ThrowAsync<JsonException>();
	}

	[Test]
	public void ReadRows_MalformedBody_ThrowsJsonException()
	{
		var reader = CreateReader();
		using var stream = new MemoryStream("""{"unexpected":true}"""u8.ToArray());

		var act = () => reader.ReadRows<LogEntry>(stream);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_StreamFailsDuringValuesFirstDrain_PropagatesException()
	{
		var reader = CreateReader();
		using var stream = new FaultingStream("""{"values":[["a",1]],"""u8.ToArray());

		using var results = reader.ReadRows<SimpleDocument>(stream);
		var act = () => results.Rows.ToList();

		act.Should().Throw<IOException>();
	}

	[Test]
	public async Task ReadRowsAsync_StreamFailsDuringValuesFirstDrain_PropagatesException()
	{
		var reader = CreateReader();
		using var stream = new FaultingStream("""{"values":[["a",1]],"""u8.ToArray());

		await using var results = await reader.ReadRowsAsync<SimpleDocument>(stream);
		var act = async () =>
		{
			await foreach (var _ in results.Rows)
			{
			}
		};

		await act.Should().ThrowAsync<IOException>();
	}

	/// <summary>Returns the seed data on the first read, then throws.</summary>
	private sealed class FaultingStream(byte[] seed) : Stream
	{
		private bool _seedReturned;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_seedReturned)
				throw new IOException("Simulated transport failure.");

			_seedReturned = true;
			seed.CopyTo(buffer, offset);
			return seed.Length;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
