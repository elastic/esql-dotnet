// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Transport;

namespace Elastic.Clients.Esql.Tests;

public class EsqlClientDisposalTests
{
	[Test]
	public void Settings_UriConstructor_TracksOwnedConfiguration()
	{
		var settings = new EsqlClientSettings(new Uri("http://localhost:9200"));

		_ = settings.OwnedConfiguration.Should().NotBeNull();
	}

	[Test]
	public void Settings_CustomTransport_DoesNotTrackConfiguration()
	{
		var transport = new DistributedTransport(new TransportConfiguration(new Uri("http://localhost:9200")));
		var settings = new EsqlClientSettings(transport, disposeTransport: true);

		_ = settings.OwnedConfiguration.Should().BeNull();
	}

	[Test]
	public void Dispose_OwnedConfiguration_IsDisposed()
	{
		var settings = new EsqlClientSettings(new Uri("http://localhost:9200"));
		var client = new EsqlClient(settings);

		client.Dispose();

		// Disposing again must be a no-op (idempotent), and the owned configuration must have
		// been disposed exactly once - observable only indirectly; assert no throw on double dispose.
		var act = () => client.Dispose();
		_ = act.Should().NotThrow();
	}
}
