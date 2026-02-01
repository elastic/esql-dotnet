// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;

namespace Elastic.Mapping.Generator.Model;

/// <summary>
/// Represents a property's mapping information extracted from source.
/// Must be equatable for incremental generator caching.
/// </summary>
internal sealed record PropertyMappingModel(
	string PropertyName,
	string FieldName,
	string FieldType,
	bool IsIgnored,
	ImmutableDictionary<string, string?> Options
)
{
	public static PropertyMappingModel Create(
		string propertyName,
		string fieldName,
		string fieldType,
		bool isIgnored = false,
		ImmutableDictionary<string, string?>? options = null
	) =>
		new(propertyName, fieldName, fieldType, isIgnored, options ?? ImmutableDictionary<string, string?>.Empty);
}

/// <summary>
/// Elasticsearch field type names.
/// </summary>
internal static class FieldTypes
{
	public const string Keyword = "keyword";
	public const string Text = "text";
	public const string Long = "long";
	public const string Integer = "integer";
	public const string Short = "short";
	public const string Byte = "byte";
	public const string Double = "double";
	public const string Float = "float";
	public const string HalfFloat = "half_float";
	public const string ScaledFloat = "scaled_float";
	public const string Date = "date";
	public const string Boolean = "boolean";
	public const string Object = "object";
	public const string Nested = "nested";
	public const string Ip = "ip";
	public const string GeoPoint = "geo_point";
	public const string GeoShape = "geo_shape";
	public const string Completion = "completion";
	public const string DenseVector = "dense_vector";
	public const string SemanticText = "semantic_text";
}
