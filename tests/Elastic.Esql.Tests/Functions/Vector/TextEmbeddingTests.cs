// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Vector;

public class TextEmbeddingTests : EsqlTestBase
{
	[Test]
	public void TextEmbedding_AsKnnArgument_EmitsNestedCall()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, EsqlFunctions.TextEmbedding("query text", "embed-1")))
			.ToString();

		_ = esql.Should().Contain("KNN(titleVec, TEXT_EMBEDDING(\"query text\", \"embed-1\"))");
	}

	[Test]
	public void InferenceEndpoints_TextEmbedding_HasWellKnownIds()
	{
		_ = InferenceEndpoints.TextEmbedding.ElserV2.Should().Be(".elser-v2-elasticsearch");
		_ = InferenceEndpoints.TextEmbedding.MultilingualE5Small.Should().Be(".multilingual-e5-small-elasticsearch");
	}
}
