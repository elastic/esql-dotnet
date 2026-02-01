// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property as a full-text searchable text field.
/// String properties default to keyword; use this attribute for full-text search.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TextAttribute : Attribute
{
	/// <summary>Analyzer for indexing.</summary>
	public string? Analyzer { get; init; }

	/// <summary>Analyzer for search queries (defaults to Analyzer if not specified).</summary>
	public string? SearchAnalyzer { get; init; }

	/// <summary>Whether to store field length norms for scoring. Set to false to disable (default: true).</summary>
	public bool Norms { get; init; } = true;

	/// <summary>Whether the field is searchable. Set to false to disable (default: true).</summary>
	public bool Index { get; init; } = true;
}
