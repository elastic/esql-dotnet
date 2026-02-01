// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marker interface for types that have Elasticsearch context.
/// The generated code provides a static `Context` property directly on the type.
/// </summary>
/// <remarks>
/// <para>
/// This interface exists primarily as a compile-time constraint for generic types.
/// The actual context is accessed via the generated static `Context` property on
/// the implementing type (e.g., `Product.Context`).
/// </para>
/// <para>
/// This pattern is similar to System.Text.Json's IJsonOnSerializing/IJsonOnSerialized
/// marker interfaces, enabling compile-time constraints without reflection.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Generated code pattern:
/// public partial class Product : IHasElasticsearchContext
/// {
///     public static ElasticsearchTypeContext Context => ElasticsearchContext.Instance;
///
///     public static class ElasticsearchContext
///     {
///         public static readonly ElasticsearchTypeContext Instance = new(...);
///         // ... hash, settings, mappings, etc.
///     }
/// }
///
/// // Usage with generic constraint:
/// public class MappingChannel&lt;T&gt; where T : class, IHasElasticsearchContext
/// {
///     // Access context via reflection-free static call pattern
/// }
/// </code>
/// </example>
public interface IHasElasticsearchContext
{
}

/// <summary>
/// Type-specific context containing all Elasticsearch metadata generated at compile time.
/// </summary>
/// <param name="GetSettingsJson">Function that returns the index settings JSON.</param>
/// <param name="GetMappingsJson">Function that returns the mappings JSON.</param>
/// <param name="GetIndexJson">Function that returns the complete index JSON (settings + mappings).</param>
/// <param name="Hash">Combined hash of settings and mappings for change detection.</param>
/// <param name="SettingsHash">Hash of settings JSON only.</param>
/// <param name="MappingsHash">Hash of mappings JSON only.</param>
/// <param name="IndexStrategy">Write target configuration (alias, data stream name, date pattern).</param>
/// <param name="SearchStrategy">Search target configuration (pattern, read alias).</param>
public record ElasticsearchTypeContext(
	Func<string> GetSettingsJson,
	Func<string> GetMappingsJson,
	Func<string> GetIndexJson,
	string Hash,
	string SettingsHash,
	string MappingsHash,
	IndexStrategy? IndexStrategy,
	SearchStrategy? SearchStrategy
);
