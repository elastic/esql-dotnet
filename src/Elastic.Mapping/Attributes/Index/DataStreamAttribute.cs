// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Specifies data stream configuration following Elastic naming: {type}-{dataset}-{namespace}.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class DataStreamAttribute : Attribute
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

	/// <summary>
	/// Gets the full data stream name: {Type}-{Dataset}-{Namespace}.
	/// </summary>
	public string FullName => $"{Type}-{Dataset}-{Namespace}";
}
