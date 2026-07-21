// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Transport;

namespace Elastic.Clients.Esql;

/// <summary>Transport-specific per-query options for the Elasticsearch executor.</summary>
public sealed record EsqlTransportOptions
{
	/// <summary>Per-request transport configuration.</summary>
	public IRequestConfiguration? RequestConfiguration { get; init; }
}
