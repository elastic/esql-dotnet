// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Vector;

public class KnnTests : EsqlTestBase
{
	[Test]
	public void Knn_WithInlineFloatArray_EmitsKnnCall()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, new float[] { 1f, 2f, 3f }))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books METADATA _score
			| WHERE KNN(titleVec, [1.0, 2.0, 3.0])
			""".NativeLineEndings());
	}

	[Test]
	public void Knn_WithCapturedClosureVector_UsesParameter()
	{
		var queryVec = new float[] { 1f, 2f, 3f };

		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, queryVec))
			.ToString();

		// Inline parameter mode (default for ToString) -> literal vector array
		_ = esql.Should().Contain("KNN(titleVec, [1.0, 2.0, 3.0])");
	}

	[Test]
	public void Knn_WithExplicitReadOnlyMemory_EmitsKnnCall()
	{
		var queryVec = new ReadOnlyMemory<float>(new float[] { 1f, 2f, 3f });

		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, queryVec))
			.ToString();

		_ = esql.Should().Contain("KNN(titleVec, [1.0, 2.0, 3.0])");
	}

	[Test]
	public void Knn_WithExplicitDenseVector_EmitsKnnCall()
	{
		var queryVec = new DenseVector<float>(new float[] { 1f, 2f, 3f });

		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, queryVec))
			.ToString();

		_ = esql.Should().Contain("KNN(titleVec, [1.0, 2.0, 3.0])");
	}

	[Test]
	public void Knn_WithOptions_EmitsNamedParameters()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, new float[] { 1f, 2f }, new KnnOptions { K = 10, MinCandidates = 100 }))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books METADATA _score
			| WHERE KNN(titleVec, [1.0, 2.0], { "k": 10, "min_candidates": 100 })
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

	[Test]
	public void Knn_WithKnnOptionsViaClosureVariable_RendersTheSameAsInline()
	{
		var options = new KnnOptions { K = 10, Similarity = 0.5 };

		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, new float[] { 1f, 2f }, options))
			.ToString();

		_ = esql.Should().Contain("KNN(titleVec, [1.0, 2.0], { \"k\": 10, \"similarity\": 0.5 })");
	}

	[Test]
	public void Knn_WithNaNInInlineVector_Throws()
	{
		var act = () => CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, new float[] { 1f, float.NaN, 3f }))
			.ToString();

		_ = act.Should().Throw<ArgumentException>().WithMessage("*NaN*");
	}

	[Test]
	public void Knn_WithInfinityInInlineVector_Throws()
	{
		var act = () => CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, new float[] { 1f, float.PositiveInfinity, 3f }))
			.ToString();

		_ = act.Should().Throw<ArgumentException>().WithMessage("*Infinity*");
	}

	[Test]
	public void Knn_WithTwoInlineVectors_DoesNotClashOrError()
	{
		// Two inline vector literals in a single Where clause should not produce parameter
		// name clashes (in inline mode they go through FormatValue directly, but the
		// translation must still succeed end-to-end).
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Where(b => EsqlFunctions.Knn(b.TitleVec, new float[] { 1f, 2f })
				|| EsqlFunctions.Knn(b.TitleVec, new float[] { 3f, 4f }))
			.ToString();

		_ = esql.Should().Contain("KNN(titleVec, [1.0, 2.0])");
		_ = esql.Should().Contain("KNN(titleVec, [3.0, 4.0])");
	}
}
