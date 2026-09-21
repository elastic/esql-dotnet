// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Tests.QueryModel;

public class EsqlQueryConstructorTests
{
	[Test]
	public void Constructor_PositionalFormat_SetsFormatNotExecutorOptions()
	{
		var query = new EsqlQuery(typeof(object), [new FromCommand("logs-*")], null, null, EsqlFormat.Csv);

		_ = query.Format.Should().Be(EsqlFormat.Csv);
		_ = query.ExecutorOptions.Should().BeNull();
	}

	[Test]
	public void Constructor_NamedExecutorOptions_RoundTrips()
	{
		var options = new object();

		var query = new EsqlQuery(typeof(object), [new FromCommand("logs-*")], null, executorOptions: options);

		_ = query.ExecutorOptions.Should().BeSameAs(options);
		_ = query.Format.Should().BeNull();
	}
}
