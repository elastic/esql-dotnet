// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

// These tests require a configured text-embedding inference endpoint on the cluster.
// Set the ELASTICSEARCH_TEXT_EMBEDDING_ENDPOINT environment variable to the inference id
// (e.g. ".elser-v2-elasticsearch") to enable. The endpoint's output dimensionality must
// match the seeded `title_vec` field's dims (4) -- otherwise the cluster will reject the
// query at runtime.

using static Elastic.Esql.Functions.EsqlFunctions;

namespace Elastic.Esql.Integration.Tests.Esql;

public class TextEmbeddingTests : IntegrationTestBase
{
	private const string EndpointEnvVar = "ELASTICSEARCH_TEXT_EMBEDDING_ENDPOINT";

	[Test]
	public async Task TextEmbedding_AsKnnArgument_ReturnsRelevantResults()
	{
		var endpoint = Environment.GetEnvironmentVariable(EndpointEnvVar);
		if (string.IsNullOrEmpty(endpoint))
			return; // skipped: requires an inference endpoint configured on the cluster

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.TitleVec, TextEmbedding("computer programming", endpoint)))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		// At least one of the top-3 hits should mention programming.
		results.Should().Contain(b => b.Title.Contains("Programming", StringComparison.OrdinalIgnoreCase));
	}

	[Test]
	public async Task TextEmbedding_StandaloneEval_ReturnsDenseVector()
	{
		var endpoint = Environment.GetEnvironmentVariable(EndpointEnvVar);
		if (string.IsNullOrEmpty(endpoint))
			return;

		// Use TEXT_EMBEDDING in an EVAL projection; the result is a dense_vector column
		// that we materialise into a ReadOnlyMemory<float> property on a small projection type.
		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex)
			.Where(b => b.Id == "book-01")
			.Select(b => new { b.Id, Embedding = TextEmbedding("hello world", endpoint) })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		results[0].Embedding.IsEmpty.Should().BeFalse();
	}
}
