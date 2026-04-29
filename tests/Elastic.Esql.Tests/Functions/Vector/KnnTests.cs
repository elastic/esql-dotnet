// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Vectors;

namespace Elastic.Esql.Tests.Functions.Vector;

public class KnnTests : EsqlTestBase
{
	[Test]
	public void Knn_WithInlineFloatArray_EmitsKnnCall()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, new float[] { 0.1f, 0.2f, 0.3f }))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books METADATA _score
			| WHERE KNN(titleVec, [0.1, 0.2, 0.3])
			""".NativeLineEndings());
	}

	[Test]
	public void Knn_WithCapturedClosureVector_UsesParameter()
	{
		var queryVec = new float[] { 0.1f, 0.2f, 0.3f };

		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, queryVec))
			.ToString();

		// Inline parameter mode (default for ToString) -> literal vector array
		_ = esql.Should().Contain("KNN(titleVec, [0.1, 0.2, 0.3])");
	}

	[Test]
	public void Knn_WithExplicitFloatVector_EmitsKnnCall()
	{
		var queryVec = new FloatVector(new float[] { 1f, 2f, 3f });

		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, queryVec))
			.ToString();

		_ = esql.Should().Contain("KNN(titleVec, [1, 2, 3])");
	}

	[Test]
	public void Knn_WithOptions_EmitsNamedParameters()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, new float[] { 1f, 2f }, new { k = 10, num_candidates = 100 }))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books METADATA _score
			| WHERE KNN(titleVec, [1, 2], { "k": 10, "num_candidates": 100 })
			""".NativeLineEndings());
	}

	[Test]
	public void Knn_OnByteVector_EmitsKnnCall()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.RgbVector, new byte[] { 0, 120, 0 }))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books METADATA _score
			| WHERE KNN(rgbVector, [0, 120, 0])
			""".NativeLineEndings());
	}

	[Test]
	public void Knn_WithTextEmbedding_GeneratesNestedCall()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, EsqlFunctions.TextEmbedding("vegan recipes", "my-embed-endpoint")))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books METADATA _score
			| WHERE KNN(titleVec, TEXT_EMBEDDING("vegan recipes", "my-embed-endpoint"))
			""".NativeLineEndings());
	}
}
