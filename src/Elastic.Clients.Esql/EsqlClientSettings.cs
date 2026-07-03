// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Elastic.Esql.Core;
using Elastic.Transport;

namespace Elastic.Clients.Esql;

/// <summary>Settings for the ES|QL client.</summary>
public class EsqlClientSettings
{
	/// <summary>The HTTP transport to use for all requests.</summary>
	public ITransport Transport { get; }

	/// <summary>Whether the client created <see cref="Transport"/> (or was granted ownership) and must dispose it.</summary>
	internal bool OwnsTransport { get; }

	/// <summary>Default query options applied to all queries unless overridden.</summary>
	public EsqlQueryDefaults Defaults { get; init; } = new();

	/// <summary>The <see cref="System.Text.Json.JsonSerializerOptions"/> used for materializing ES|QL results.</summary>
	public JsonSerializerOptions? JsonSerializerOptions { get; init; }

	/// <summary>
	/// A source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> for AOT-compatible serialization.
	/// When set, takes precedence over <see cref="JsonSerializerOptions"/>.
	/// </summary>
	public JsonSerializerContext? JsonSerializerContext { get; init; }

	/// <summary>Optional interceptor invoked after translation but before formatting and execution of every query.</summary>
	public IEsqlQueryInterceptor? Interceptor { get; init; }

	/// <summary>Creates settings with a node URI.</summary>
	public EsqlClientSettings(Uri nodeUri)
	{
		var config = new TransportConfiguration(
			nodeUri ?? throw new ArgumentNullException(nameof(nodeUri)),
			productRegistration: EsqlProductRegistration.Default
		);
		Transport = new DistributedTransport(config);
		OwnsTransport = true;
	}

	/// <summary>
	/// Creates settings with a custom transport.
	/// The provided transport is responsible for its own product registration;
	/// see <see cref="EsqlProductRegistration.Default"/> for the recommended value.
	/// </summary>
	/// <param name="transport">The transport to use for all requests.</param>
	/// <param name="disposeTransport">
	/// Set <see langword="true"/> to transfer ownership so disposing the client disposes the transport.
	/// Defaults to <see langword="false"/> because an externally created transport is typically shared with other clients.
	/// </param>
	public EsqlClientSettings(ITransport transport, bool disposeTransport = false)
	{
		Transport = transport ?? throw new ArgumentNullException(nameof(transport));
		OwnsTransport = disposeTransport;
	}

	/// <summary>Creates settings with a connection pool.</summary>
	public EsqlClientSettings(NodePool nodePool)
	{
		var config = new TransportConfiguration(
			nodePool ?? throw new ArgumentNullException(nameof(nodePool)),
			productRegistration: EsqlProductRegistration.Default
		);
		Transport = new DistributedTransport(config);
		OwnsTransport = true;
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
