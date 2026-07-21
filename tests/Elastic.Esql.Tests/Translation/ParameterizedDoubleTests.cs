// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class ParameterizedDoubleTests : EsqlTestBase
{
	[Test]
	public void Where_CapturedWholeDouble_ParameterKeepsDecimalPoint()
	{
		var threshold = 100.0;

		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration / threshold > 1);

		_ = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		_ = parameters.Parameters["threshold"].GetRawText().Should().Be("100.0");
	}

	[Test]
	public void Where_CapturedFractionalDouble_ParameterUnchanged()
	{
		var threshold = 99.5;

		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration / threshold > 1);

		_ = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		_ = parameters.Parameters["threshold"].GetRawText().Should().Be("99.5");
	}

	[Test]
	public void Row_CapturedWholeDoubleArray_ParameterKeepsDecimalPoints()
	{
		var values = new[] { 100.0, 200.5 };

		var query = CreateQuery<LogEntry>()
			.Row(() => new { vals = values });

		_ = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		_ = parameters.Parameters["vals"].GetRawText().Should().Be("[100.0,200.5]");
	}

	[Test]
	public void Row_CapturedFloatList_ParameterKeepsDecimalPoints()
	{
		var values = new List<float> { 1f, 2.5f };

		var query = CreateQuery<LogEntry>()
			.Row(() => new { vals = values });

		_ = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		_ = parameters.Parameters["vals"].GetRawText().Should().Be("[1.0,2.5]");
	}
}
