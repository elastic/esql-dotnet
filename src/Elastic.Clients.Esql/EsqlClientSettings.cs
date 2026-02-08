// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql;
using Elastic.Mapping;
using Elastic.Transport;

namespace Elastic.Clients.Esql;

/// <summary>Settings for the ES|QL client.</summary>
public class EsqlClientSettings
{
	/// <summary>The HTTP transport to use for all requests.</summary>
	public ITransport Transport { get; }

	/// <summary>Default query options applied to all queries unless overridden.</summary>
	public EsqlQueryDefaults Defaults { get; init; } = new();

	/// <summary>The mapping context providing type metadata for field resolution.</summary>
	public IElasticsearchMappingContext? MappingContext { get; init; }

	/// <summary>Creates settings with a node URI.</summary>
	public EsqlClientSettings(Uri nodeUri)
	{
		ArgumentNullException.ThrowIfNull(nodeUri);
		var config = new TransportConfiguration(nodeUri);
		Transport = new DistributedTransport(config);
	}

	/// <summary>Creates settings with a custom transport.</summary>
	public EsqlClientSettings(ITransport transport)
	{
		Transport = transport ?? throw new ArgumentNullException(nameof(transport));
	}

	/// <summary>Creates settings with a connection pool.</summary>
	public EsqlClientSettings(NodePool nodePool)
	{
		ArgumentNullException.ThrowIfNull(nodePool);
		var config = new TransportConfiguration(nodePool);
		Transport = new DistributedTransport(config);
	}

	/// <summary>Creates in-memory settings for string generation only.</summary>
	private EsqlClientSettings(IElasticsearchMappingContext? mappingContext = null)
	{
		var pool = new SingleNodePool(new Uri("http://localhost:9200"));
		var config = new TransportConfiguration(pool, new InMemoryRequestInvoker());
		Transport = new DistributedTransport(config);
		MappingContext = mappingContext;
	}

	/// <summary>
	/// Creates settings for in-memory/string generation only usage.
	/// No actual Elasticsearch connection is made.
	/// </summary>
	public static EsqlClientSettings InMemory(IElasticsearchMappingContext? mappingContext = null) =>
		new(mappingContext);
}
