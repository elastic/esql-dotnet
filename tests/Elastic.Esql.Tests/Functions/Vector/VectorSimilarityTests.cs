// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Functions.Vector;

public class VectorSimilarityTests : EsqlTestBase
{
	[Test]
	public void VCosine_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books")
			.Where(b => EsqlFunctions.VCosine(b.TitleVec, new float[] { 0f, 255f, 255f }) > 0.5)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books
			| WHERE V_COSINE(titleVec, [0, 255, 255]) > 0.5
			""".NativeLineEndings());
	}

	[Test]
	public void VDotProduct_InWhere_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books")
			.Where(b => EsqlFunctions.VDotProduct(b.TitleVec, new float[] { 1f, 2f }) > 0)
			.ToString();

		_ = esql.Should().Contain("V_DOT_PRODUCT(titleVec, [1, 2])");
	}

	[Test]
	public void VHamming_OnByteVector_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books")
			.Where(b => EsqlFunctions.VHamming(b.RgbVector, new float[] { 1, 2, 3 }) < 10)
			.ToString();

		_ = esql.Should().Contain("V_HAMMING(rgbVector, [1, 2, 3])");
	}

	[Test]
	public void VL1Norm_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books")
			.Where(b => EsqlFunctions.VL1Norm(b.TitleVec, new float[] { 1f, 2f }) > 0)
			.ToString();

		_ = esql.Should().Contain("V_L1_NORM(titleVec, [1, 2])");
	}

	[Test]
	public void VL2Norm_GeneratesCorrectEsql()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books")
			.Where(b => EsqlFunctions.VL2Norm(b.TitleVec, new float[] { 1f, 2f }) > 0)
			.ToString();

		_ = esql.Should().Contain("V_L2_NORM(titleVec, [1, 2])");
	}
}
