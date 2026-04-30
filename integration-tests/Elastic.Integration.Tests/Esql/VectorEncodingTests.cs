// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Serialization;
using Elastic.Esql.Vectors;
using Elastic.Transport;

using static Elastic.Esql.Functions.EsqlFunctions;

namespace Elastic.Esql.Integration.Tests.Esql;

/// <summary>
/// Verifies that the wire encoding for <see cref="FloatVector"/> / <see cref="ByteVector"/>
/// request parameters is server-acceptable for every supported encoding mode (against ES 9.3+).
/// </summary>
/// <remarks>
/// As of Elasticsearch 9.3.3, the ES|QL <c>params</c> parser only accepts:
/// <list type="bullet">
/// <item>JSON arrays for float dense_vector parameters (i.e. <see cref="FloatVectorEncoding.Legacy"/>).</item>
/// <item>JSON arrays of signed bytes (<see cref="ByteVectorEncoding.Legacy"/>) or hex strings
/// (<see cref="ByteVectorEncoding.Hex"/>) for byte dense_vector parameters.</item>
/// </list>
/// Base64-encoded vectors are valid only at index time, not as ES|QL query parameters.
/// Tests for the base64 modes are intentionally omitted here -- their wire-format correctness
/// is covered by the unit-level converter round-trip tests.
/// </remarks>
public class VectorEncodingTests : IntegrationTestBase
{
	[Test]
	public async Task FloatVector_Legacy_ProducesCorrectKnnResults()
	{
		using var client = ClientWith(FloatVectorEncoding.Legacy, ByteVectorEncoding.Legacy);
		var query = new float[] { 1f, 0f, 0f, 0f };

		var results = await client
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.TitleVec, query))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(1)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		results[0].Id.Should().Be("book-01");
	}

	[Test]
	public async Task ByteVector_Legacy_ProducesCorrectKnnResults()
	{
		using var client = ClientWith(FloatVectorEncoding.Legacy, ByteVectorEncoding.Legacy);
		var query = new byte[] { 255, 0, 0 };

		var results = await client
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.RgbVector, query))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(1)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		results[0].Id.Should().Be("book-01");
	}

	[Test]
	public async Task ByteVector_Hex_ProducesCorrectKnnResults()
	{
		using var client = ClientWith(FloatVectorEncoding.Legacy, ByteVectorEncoding.Hex);
		var query = new byte[] { 0, 255, 0 };

		var results = await client
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.RgbVector, query))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(1)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		results[0].Id.Should().Be("book-04");
	}

	[Test]
	public async Task FloatVector_ReadFromIndex_RoundtripsExactValues()
	{
		// Materialise a TestBook -- the FloatVector / ByteVector properties should round-trip.
		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex)
			.Where(b => b.Id == "book-01")
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		var book = results[0];

		book.TitleVec.Length.Should().Be(4);
		book.TitleVec.Data.ToArray().Should().Equal([1f, 0f, 0f, 0f]);

		book.RgbVector.Length.Should().Be(3);
		book.RgbVector.Data.ToArray().Should().Equal((byte[])[255, 0, 0]);
	}

	private EsqlClient ClientWith(FloatVectorEncoding floatEncoding, ByteVectorEncoding byteEncoding) =>
		new(new EsqlClientSettings(Fixture.EsqlClient.Settings.Transport)
		{
			JsonSerializerContext = IntegrationJsonContext.Default,
			FloatVectorEncoding = floatEncoding,
			ByteVectorEncoding = byteEncoding
		});
}
