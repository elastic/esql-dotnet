// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;

namespace Elastic.Mapping;

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
/// <param name="ConfigureAnalysis">Optional delegate for configuring analysis settings at runtime.</param>
public record ElasticsearchTypeContext(
	Func<string> GetSettingsJson,
	Func<string> GetMappingsJson,
	Func<string> GetIndexJson,
	string Hash,
	string SettingsHash,
	string MappingsHash,
	IndexStrategy? IndexStrategy,
	SearchStrategy? SearchStrategy,
	Func<AnalysisBuilder, AnalysisBuilder>? ConfigureAnalysis = null,
	Type? MappedType = null
);
