// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Transport;

namespace Elastic.Esql;

/// <summary>
/// Settings for the ES|QL client.
/// </summary>
public class EsqlClientSettings
{
	/// <summary>
	/// The Elasticsearch node URI.
	/// </summary>
	public Uri NodeUri { get; }

	/// <summary>
	/// The HTTP transport to use.
	/// </summary>
	public ITransport Transport { get; }

	/// <summary>
	/// The default request timeout.
	/// </summary>
	public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(1);

	/// <summary>
	/// Whether to include profile information in queries.
	/// </summary>
	public bool IncludeProfile { get; set; }

	/// <summary>
	/// Whether to use columnar format for responses.
	/// </summary>
	public bool Columnar { get; set; }

	/// <summary>
	/// Creates settings with a node URI.
	/// </summary>
	public EsqlClientSettings(Uri nodeUri)
	{
		NodeUri = nodeUri ?? throw new ArgumentNullException(nameof(nodeUri));
		var config = new TransportConfiguration(nodeUri);
		Transport = new DistributedTransport(config);
	}

	/// <summary>
	/// Creates settings with a custom transport.
	/// </summary>
	public EsqlClientSettings(ITransport transport, Uri nodeUri)
	{
		Transport = transport ?? throw new ArgumentNullException(nameof(transport));
		NodeUri = nodeUri ?? throw new ArgumentNullException(nameof(nodeUri));
	}

	/// <summary>
	/// Creates settings with a connection pool.
	/// </summary>
	public EsqlClientSettings(NodePool nodePool)
	{
		ArgumentNullException.ThrowIfNull(nodePool);
		var config = new TransportConfiguration(nodePool);
		Transport = new DistributedTransport(config);
		NodeUri = nodePool.Nodes.FirstOrDefault()?.Uri ?? new Uri("http://localhost:9200");
	}

	/// <summary>
	/// Creates in-memory settings for string generation only.
	/// </summary>
	private EsqlClientSettings()
	{
		NodeUri = new Uri("http://localhost:9200");
		var pool = new SingleNodePool(NodeUri);
		var config = new TransportConfiguration(pool, new InMemoryRequestInvoker());
		Transport = new DistributedTransport(config);
	}

	/// <summary>
	/// Creates settings for in-memory/string generation only usage.
	/// No actual Elasticsearch connection is made.
	/// </summary>
	public static EsqlClientSettings InMemory() => new();
}
