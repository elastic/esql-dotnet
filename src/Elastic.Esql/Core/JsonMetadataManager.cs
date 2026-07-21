// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Core;

/// <summary>
/// Centralizes all <see cref="System.Text.Json"/> metadata operations (type info resolution,
/// property name lookup, converter discovery) behind a provider-scoped, thread-safe cache.
/// </summary>
internal sealed class JsonMetadataManager(JsonSerializerOptions options)
{
	private readonly ConcurrentDictionary<Type, JsonTypeInfo> _typeInfoCache = [];
	private readonly ConcurrentDictionary<Type, HashSet<string>> _propertyNamesCache = [];
	private readonly ConcurrentDictionary<Type, JsonSerializerOptions> _converterOptionsCache = [];
	private readonly ConcurrentDictionary<Type, Dictionary<string, JsonPropertyInfo>> _ctorPropertyMapCache = [];
	private readonly ConcurrentDictionary<Type, Dictionary<MemberInfo, JsonPropertyInfo>> _memberPropertyMapCache = [];

	/// <summary>The underlying <see cref="JsonSerializerOptions"/>.</summary>
	public JsonSerializerOptions Options => options;

	/// <summary>
	/// Returns the <see cref="JsonTypeInfo"/> for <paramref name="type"/>, validating that the
	/// type uses property-based serialization. Types with a type-level <see cref="JsonConverterAttribute"/>
	/// have <see cref="JsonTypeInfoKind.None"/> and cannot be used as ES|QL entity types.
	/// </summary>
	public JsonTypeInfo GetPropertyBasedTypeInfo(Type type) =>
		_typeInfoCache.GetOrAdd(type, t =>
		{
			var typeInfo = options.GetTypeInfo(t);

			if (typeInfo.Kind == JsonTypeInfoKind.None)
				throw new NotSupportedException(
					$"Type '{t.Name}' uses a custom JsonConverter and cannot be used as an ES|QL entity type. " +
					"ES|QL requires property-based serialization.");

			return typeInfo;
		});

	/// <summary>
	/// Resolves the JSON property name for a concrete type member via <see cref="JsonTypeInfo"/> metadata.
	/// </summary>
	public string ResolvePropertyName(Type type, MemberInfo member)
	{
		var propertyMap = GetMemberPropertyMap(type);
		return propertyMap.TryGetValue(member, out var property)
			? property.Name
			: throw new NotSupportedException($"Member '{member.Name}' of type '{member.DeclaringType?.Name}' is not supported.");
	}

	/// <summary>
	/// Returns all JSON property names for a type, cached for the provider lifetime.
	/// </summary>
	public HashSet<string> GetAllPropertyNames(Type type) =>
		_propertyNamesCache.GetOrAdd(type, t =>
		{
			var typeInfo = GetPropertyBasedTypeInfo(t);
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var prop in typeInfo.Properties)
				_ = names.Add(prop.Name);

			return names;
		});

	/// <summary>
	/// Returns a case-insensitive CLR-member-name to <see cref="JsonPropertyInfo"/> map for
	/// <paramref name="type"/>, cached for the provider lifetime. Used to resolve constructor
	/// parameter names to their corresponding JSON property metadata.
	/// </summary>
	public Dictionary<string, JsonPropertyInfo> GetConstructorPropertyMap(Type type) =>
		_ctorPropertyMapCache.GetOrAdd(type, t =>
		{
			var typeInfo = GetPropertyBasedTypeInfo(t);
			var map = new Dictionary<string, JsonPropertyInfo>(StringComparer.OrdinalIgnoreCase);
			foreach (var prop in typeInfo.Properties)
			{
				if (prop.AttributeProvider is MemberInfo mi)
					map[mi.Name] = prop;
			}
			return map;
		});

	/// <summary>
	/// Returns the <see cref="JsonConverter"/> explicitly set on the property identified by
	/// <paramref name="member"/>, or <see langword="null"/> if none is configured.
	/// Falls back gracefully when the declaring type is not registered in the serializer context.
	/// </summary>
	public JsonConverter? FindPropertyConverter(MemberInfo? member)
	{
		if (member?.DeclaringType is not { } declaringType)
			return null;

		try
		{
			var propertyMap = GetMemberPropertyMap(declaringType);
			return propertyMap.TryGetValue(member, out var property) ? property.CustomConverter : null;
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// Declaring type not registered: no per-property converter can apply.
			return null;
		}
	}

	/// <summary>
	/// Returns <see cref="JsonSerializerOptions"/> with the specified <paramref name="converter"/>
	/// inserted at highest priority. Results are cached by converter runtime type.
	/// </summary>
	public JsonSerializerOptions GetOptionsWithConverter(JsonConverter converter) =>
		_converterOptionsCache.GetOrAdd(converter.GetType(), _ =>
		{
			var result = new JsonSerializerOptions(options);
			result.Converters.Insert(0, converter);
			return result;
		});

	private Dictionary<MemberInfo, JsonPropertyInfo> GetMemberPropertyMap(Type type) =>
		_memberPropertyMapCache.GetOrAdd(type, t =>
		{
			var typeInfo = GetPropertyBasedTypeInfo(t);
			var result = new Dictionary<MemberInfo, JsonPropertyInfo>();

			foreach (var property in typeInfo.Properties)
			{
				if (property.AttributeProvider is MemberInfo member)
					result[member] = property;
			}

			return result;
		});
}
