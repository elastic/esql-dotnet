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
/// Uses injected mapping context or reflection-based discovery.
/// </summary>
public class FieldNameResolver(IElasticsearchMappingContext? mappingContext = null)
{
	private readonly ConcurrentDictionary<MemberInfo, string> _fieldNameCache = new();
	private readonly ConcurrentDictionary<Type, TypeFieldMetadata?> _typeMetadataCache = new();

	/// <summary>
	/// Resolves the ES|QL field name for a property.
	/// </summary>
	public string Resolve(MemberInfo member) => _fieldNameCache.GetOrAdd(member, ResolveInternal);

	/// <summary>
	/// Gets the index pattern for a type.
	/// </summary>
	public string? GetIndexPattern(Type type) => GetTypeMetadata(type)?.SearchPattern;

	/// <summary>
	/// Checks if a property should be ignored.
	/// </summary>
	public bool IsIgnored(MemberInfo member)
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
	public Dictionary<string, PropertyInfo>? GetGeneratedPropertyMap(Type type) =>
		GetTypeMetadata(type)?.GetPropertyMapFunc?.Invoke();

	private TypeFieldMetadata? GetTypeMetadata(Type? type)
	{
		if (type == null)
			return null;
		return _typeMetadataCache.GetOrAdd(type, LookupMetadata);
	}

	private TypeFieldMetadata? LookupMetadata(Type type) =>
		mappingContext?.GetTypeMetadata(type) ?? DiscoverMetadata(type);

	private string ResolveInternal(MemberInfo member)
	{
		// Try registered/discovered metadata first
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

	private static TypeFieldMetadata? DiscoverMetadata(Type type)
	{
		// Try to find the legacy ElasticsearchContext nested type on the domain type
		var contextType = type.GetNestedType("ElasticsearchContext", BindingFlags.Public | BindingFlags.Static);
		if (contextType != null)
			return DiscoverFromNestedContext(contextType);

		// No metadata found — type may not have been registered yet
		return null;
	}

	private static TypeFieldMetadata? DiscoverFromNestedContext(Type contextType)
	{
		var fieldMappingType = contextType.GetNestedType("FieldMapping", BindingFlags.Public | BindingFlags.Static);
		if (fieldMappingType == null)
			return null;

		var propertyToFieldValue = fieldMappingType
			.GetField("PropertyToField", BindingFlags.Public | BindingFlags.Static)
			?.GetValue(null);
		var propertyToField = propertyToFieldValue is IReadOnlyDictionary<string, string> ptf ? ptf : null;

		var ignoredPropsValue = contextType
			.GetField("IgnoredProperties", BindingFlags.Public | BindingFlags.Static)
			?.GetValue(null);
		var ignoredProps = ignoredPropsValue is IReadOnlySet<string> ip ? ip : null;

		string? searchPattern = null;
		var searchStrategyProp = contextType.GetProperty("SearchStrategy", BindingFlags.Public | BindingFlags.Static);
		if (searchStrategyProp != null)
		{
			var strategy = searchStrategyProp.GetValue(null);
			if (strategy is SearchStrategy ss)
				searchPattern = ss.Pattern;
		}

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
