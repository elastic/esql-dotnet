// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Esql.Execution;
using Elastic.Transport;

namespace Elastic.Clients.Esql.Tests;

internal static class TestExecutorFactory
{
	public static EsqlClientSettings CreateSettings(
		CapturingRequestInvoker invoker,
		EsqlQueryDefaults? defaults = null)
	{
		var config = new TransportConfiguration(
			new SingleNodePool(new Uri("http://localhost:9200")),
			invoker,
			productRegistration: EsqlProductRegistration.Default);

		return new EsqlClientSettings(new DistributedTransport(config))
		{
			Defaults = defaults ?? new EsqlQueryDefaults()
		};
	}

	public static (EsqlTransportExecutor Executor, CapturingRequestInvoker Invoker) Create(
		byte[]? responseBody = null,
		int statusCode = 200,
		EsqlQueryDefaults? defaults = null)
	{
		var invoker = new CapturingRequestInvoker(responseBody ?? """{"columns":[],"values":[]}"""u8.ToArray(), statusCode);
		var settings = CreateSettings(invoker, defaults);
		return (new EsqlTransportExecutor(settings), invoker);
	}
}
