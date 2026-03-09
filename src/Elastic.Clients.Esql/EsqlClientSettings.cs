// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql;
using Elastic.Transport;

namespace Elastic.Clients.Esql;

/// <summary>Settings for the ES|QL client.</summary>
public class EsqlClientSettings
{
	/// <summary>The HTTP transport to use for all requests.</summary>
	public ITransport Transport { get; }

	/// <summary>Default query options applied to all queries unless overridden.</summary>
	public EsqlQueryDefaults Defaults { get; init; } = new();

	/// <summary>The <see cref="System.Text.Json.JsonSerializerOptions"/> used for materializing ES|QL results.</summary>
	public JsonSerializerOptions? JsonSerializerOptions { get; init; }

	/// <summary>
	/// A source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> for AOT-compatible serialization.
	/// When set, takes precedence over <see cref="JsonSerializerOptions"/>.
	/// </summary>
	public JsonSerializerContext? JsonSerializerContext { get; init; }

	/// <summary>Creates settings with a node URI.</summary>
	public EsqlClientSettings(Uri nodeUri)
	{
		var config = new TransportConfiguration(nodeUri ?? throw new ArgumentNullException(nameof(nodeUri)));
		Transport = new DistributedTransport(config);
	}

	/// <summary>Creates settings with a custom transport.</summary>
	public EsqlClientSettings(ITransport transport) =>
		Transport = transport ?? throw new ArgumentNullException(nameof(transport));

	/// <summary>Creates settings with a connection pool.</summary>
	public EsqlClientSettings(NodePool nodePool)
	{
		var config = new TransportConfiguration(nodePool ?? throw new ArgumentNullException(nameof(nodePool)));
		Transport = new DistributedTransport(config);
	}

	/// <summary>Resolves the effective <see cref="System.Text.Json.JsonSerializerOptions"/> from context or explicit options.</summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "DefaultJsonTypeInfoResolver is a fallback; the user-provided JsonSerializerContext is expected to include an AOT-safe TypeInfoResolver.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DefaultJsonTypeInfoResolver is a fallback; the user-provided JsonSerializerContext is expected to include an AOT-safe TypeInfoResolver.")]
	internal JsonSerializerOptions ResolveJsonOptions()
	{
		if (JsonSerializerContext is not null)
		{
			return new JsonSerializerOptions
			{
				TypeInfoResolver = JsonTypeInfoResolver.Combine(
					JsonSerializerContext,
					new DefaultJsonTypeInfoResolver()
				),
				PropertyNamingPolicy = JsonSerializerContext.Options.PropertyNamingPolicy ?? JsonNamingPolicy.CamelCase
			};
		}

		return JsonSerializerOptions ?? CreateDefaultJsonOptions();
	}

	private static JsonSerializerOptions CreateDefaultJsonOptions() =>
		new(JsonSerializerOptions.Default)
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};
}
