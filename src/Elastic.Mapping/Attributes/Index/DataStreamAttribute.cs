// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Registers a type as an Elasticsearch data stream within an <see cref="ElasticsearchMappingContextAttribute"/> context.
/// Applied to the context class, not the domain type.
/// Data streams follow the Elastic naming convention: {type}-{dataset}-{namespace}.
/// </summary>
/// <typeparam name="T">The domain type to map to an Elasticsearch data stream.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DataStreamAttribute<T> : Attribute where T : class
{
	/// <summary>
	/// Data stream type (e.g., "logs", "metrics", "traces", "synthetics").
	/// </summary>
	public required string Type { get; init; }

	/// <summary>
	/// Dataset identifier (e.g., "nginx.access", "system.cpu").
	/// </summary>
	public required string Dataset { get; init; }

	/// <summary>
	/// Namespace for environment separation (e.g., "production", "development").
	/// Defaults to "default".
	/// </summary>
	public string Namespace { get; init; } = "default";

	/// <summary>Optional static class containing ConfigureAnalysis/ConfigureMappings methods.</summary>
	public Type? Configuration { get; init; }
}
