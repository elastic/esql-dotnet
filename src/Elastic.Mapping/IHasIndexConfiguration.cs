// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping;

#if NET8_0_OR_GREATER
/// <summary>
/// Interface for types that provide analysis configuration.
/// Implemented as static abstract members for AOT-compatible auto-discovery.
/// </summary>
public interface IHasAnalysisConfiguration
{
	/// <summary>
	/// Configures analysis settings for the index.
	/// The model receives the builder and returns it - no explicit Build() call needed.
	/// </summary>
	static abstract AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis);
}

/// <summary>
/// Interface for types that provide mappings configuration.
/// TBuilder is the source-generated type-specific mappings builder.
/// </summary>
/// <typeparam name="TBuilder">The type-specific mappings builder generated for this model.</typeparam>
public interface IHasMappingsConfiguration<TBuilder> where TBuilder : MappingsBuilderBase<TBuilder>, new()
{
	/// <summary>
	/// Configures mapping overrides for the index.
	/// The model receives the builder and returns it - no explicit Build() call needed.
	/// </summary>
	static abstract TBuilder ConfigureMappings(TBuilder mappings);
}

/// <summary>
/// Combined interface for types that provide both analysis and mappings configuration.
/// </summary>
/// <typeparam name="TBuilder">The type-specific mappings builder generated for this model.</typeparam>
public interface IHasIndexConfiguration<TBuilder> : IHasAnalysisConfiguration, IHasMappingsConfiguration<TBuilder>
	where TBuilder : MappingsBuilderBase<TBuilder>, new()
{
}
#endif
