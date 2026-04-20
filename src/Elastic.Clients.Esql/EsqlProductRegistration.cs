// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Clients.Esql;

/// <summary>
/// Product registration for the ES|QL client.
/// Wires the <see cref="EsqlClient"/> assembly as the version marker so transport
/// meta-headers and the Elasticsearch-compatible Accept header reflect this client.
/// <para>
/// Users constructing their own <see cref="Elastic.Transport.TransportConfiguration"/>
/// (for example to configure authentication or custom HTTP settings) should pass
/// <see cref="Default"/> to benefit from the same error handling that the built-in
/// <see cref="EsqlClientSettings"/> constructors provide.
/// </para>
/// </summary>
public sealed class EsqlProductRegistration : ElasticsearchProductRegistration
{
	public EsqlProductRegistration() : base(typeof(EsqlClient)) { }

	public override MetaHeaderProvider MetaHeaderProvider { get; } =
		new DefaultMetaHeaderProvider(typeof(EsqlClient), "esql");

	public static new EsqlProductRegistration Default { get; } = new();
}
