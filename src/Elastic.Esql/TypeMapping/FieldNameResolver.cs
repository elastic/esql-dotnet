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
/// </summary>
public class FieldNameResolver
{
	private readonly ConcurrentDictionary<MemberInfo, string> _fieldNameCache = new();

	/// <summary>
	/// Resolves the ES|QL field name for a property.
	/// </summary>
	public string Resolve(MemberInfo member) => _fieldNameCache.GetOrAdd(member, ResolveInternal);

	private static string ResolveInternal(MemberInfo member)
	{
		// Check for JsonPropertyName attribute (STJ - single source of truth)
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
		// Check for [Index] attribute with SearchPattern
		var indexAttribute = type.GetCustomAttribute<IndexAttribute>();
		if (indexAttribute?.SearchPattern != null)
			return indexAttribute.SearchPattern;

		// Check for [DataStream] attribute
		var dataStreamAttribute = type.GetCustomAttribute<DataStreamAttribute>();
		if (dataStreamAttribute != null)
			return $"{dataStreamAttribute.Type}-{dataStreamAttribute.Dataset}-*";

		// Fallback to index name
		return indexAttribute?.Name;
	}

	/// <summary>
	/// Checks if a property should be ignored.
	/// </summary>
	public static bool IsIgnored(MemberInfo member) =>
		member.GetCustomAttribute<JsonIgnoreAttribute>() != null;

	private static string ToCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		if (name.Length == 1)
			return name.ToLowerInvariant();

		return char.ToLowerInvariant(name[0]) + name.Substring(1);
	}
}
