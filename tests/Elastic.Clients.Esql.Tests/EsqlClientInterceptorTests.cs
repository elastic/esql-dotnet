// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.QueryModel;
using Elastic.Transport;

namespace Elastic.Clients.Esql.Tests;

public class EsqlClientInterceptorTests
{
	[Test]
	public void Query_WithSettingsInterceptor_SendsInterceptedQuery()
	{
		var invoker = new CapturingRequestInvoker("""{"columns":[],"values":[]}"""u8.ToArray());
		var config = new TransportConfiguration(
			new SingleNodePool(new Uri("http://localhost:9200")),
			invoker,
			productRegistration: EsqlProductRegistration.Default
		);
		var settings = new EsqlClientSettings(new DistributedTransport(config))
		{
			Interceptor = new LimitInterceptor()
		};
		using var client = new EsqlClient(settings);

		_ = client.CreateQuery<ClientTestDocument>().From("idx").ToList();

		_ = invoker.LastRequestBody.Should().Contain("| LIMIT 7");
	}

	private sealed class LimitInterceptor : IEsqlQueryInterceptor
	{
		public EsqlQuery Intercept(EsqlQuery query) => query.WithLimit(7);
	}
}
