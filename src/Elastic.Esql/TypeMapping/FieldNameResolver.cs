// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using Elastic.Mapping;

namespace Elastic.Esql.TypeMapping;

/// <summary>
/// Resolves C# property names to ES|QL field names.
/// Uses generated compile-time metadata when available, falling back to reflection.
/// </summary>
public class FieldNameResolver
{
	private readonly ConcurrentDictionary<MemberInfo, string> _fieldNameCache = new();
	private static readonly ConcurrentDictionary<Type, TypeFieldMetadata?> TypeMetadataCache = new();

	/// <summary>
	/// Resolves the ES|QL field name for a property.
	/// </summary>
	public string Resolve(MemberInfo member) => _fieldNameCache.GetOrAdd(member, ResolveInternal);

	private static string ResolveInternal(MemberInfo member)
	{
		// Try generated metadata first (one-time reflection per type to find ElasticsearchContext)
		var metadata = GetTypeMetadata(member.DeclaringType);
		if (metadata?.PropertyToField.TryGetValue(member.Name, out var fieldName) == true)
			return fieldName;

		// Fallback to JsonPropertyName attribute for non-generated types
		var jsonPropertyName = member.GetCustomAttribute<JsonPropertyNameAttribute>();
		if (jsonPropertyName != null)
			return jsonPropertyName.Name;

		// Default to camelCase
		return ToCamelCase(member.Name);
	}

	/// <summary>
	/// Gets the index pattern for a type.
	/// </summary>
	public static string? GetIndexPattern(Type type)
	{
		// Try generated metadata first
		var metadata = GetTypeMetadata(type);
		if (metadata?.SearchPattern != null)
			return metadata.SearchPattern;

		// Fallback for non-generated types
		var indexAttribute = type.GetCustomAttribute<IndexAttribute>();
		if (indexAttribute?.SearchPattern != null)
			return indexAttribute.SearchPattern;

		var dataStreamAttribute = type.GetCustomAttribute<DataStreamAttribute>();
		if (dataStreamAttribute != null)
			return $"{dataStreamAttribute.Type}-{dataStreamAttribute.Dataset}-*";

		return indexAttribute?.Name;
	}

	/// <summary>
	/// Checks if a property should be ignored.
	/// </summary>
	public static bool IsIgnored(MemberInfo member)
	{
		var metadata = GetTypeMetadata(member.DeclaringType);
		if (metadata != null)
			return metadata.IgnoredProperties.Contains(member.Name);

		// Fallback for non-generated types
		return member.GetCustomAttribute<JsonIgnoreAttribute>() != null;
	}

	/// <summary>
	/// Gets the generated property map for a type if available.
	/// </summary>
	public static Dictionary<string, PropertyInfo>? GetGeneratedPropertyMap(Type type)
	{
		var metadata = GetTypeMetadata(type);
		return metadata?.GetPropertyMapFunc?.Invoke();
	}

	private static TypeFieldMetadata? GetTypeMetadata(Type? type)
	{
		if (type == null)
			return null;
		return TypeMetadataCache.GetOrAdd(type, DiscoverMetadata);
	}

	private static TypeFieldMetadata? DiscoverMetadata(Type type)
	{
		// One-time reflection to find the generated ElasticsearchContext class
		var contextType = type.GetNestedType("ElasticsearchContext", BindingFlags.Public | BindingFlags.Static);
		if (contextType == null)
			return null;

		var fieldMappingType = contextType.GetNestedType("FieldMapping", BindingFlags.Public | BindingFlags.Static);
		if (fieldMappingType == null)
			return null;

		// Get the generated dictionaries using pattern matching
		var propertyToFieldValue = fieldMappingType
			.GetField("PropertyToField", BindingFlags.Public | BindingFlags.Static)
			?.GetValue(null);
		var propertyToField = propertyToFieldValue is IReadOnlyDictionary<string, string> ptf ? ptf : null;

		var ignoredPropsValue = contextType
			.GetField("IgnoredProperties", BindingFlags.Public | BindingFlags.Static)
			?.GetValue(null);
		var ignoredProps = ignoredPropsValue is IReadOnlySet<string> ip ? ip : null;

		// Get search pattern from SearchStrategy
		string? searchPattern = null;
		var searchStrategyProp = contextType.GetProperty("SearchStrategy", BindingFlags.Public | BindingFlags.Static);
		if (searchStrategyProp != null)
		{
			var strategy = searchStrategyProp.GetValue(null);
			if (strategy is SearchStrategy ss)
				searchPattern = ss.Pattern;
		}

		// Get the GetPropertyMap method
		Func<Dictionary<string, PropertyInfo>>? getPropertyMapFunc = null;
		var getPropertyMapMethod = contextType.GetMethod("GetPropertyMap", BindingFlags.Public | BindingFlags.Static);
		if (getPropertyMapMethod != null)
			getPropertyMapFunc = () => (Dictionary<string, PropertyInfo>)getPropertyMapMethod.Invoke(null, null)!;

		if (propertyToField == null)
			return null;

		return new TypeFieldMetadata(
			propertyToField,
			ignoredProps ?? new HashSet<string>(),
			searchPattern,
			getPropertyMapFunc
		);
	}

	private static string ToCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		if (name.Length == 1)
			return name.ToLowerInvariant();

		return char.ToLowerInvariant(name[0]) + name.Substring(1);
	}
}

/// <summary>
/// Cached metadata for a type's field mappings discovered from generated code.
/// </summary>
internal sealed record TypeFieldMetadata(
	IReadOnlyDictionary<string, string> PropertyToField,
	IReadOnlySet<string> IgnoredProperties,
	string? SearchPattern,
	Func<Dictionary<string, PropertyInfo>>? GetPropertyMapFunc
);
