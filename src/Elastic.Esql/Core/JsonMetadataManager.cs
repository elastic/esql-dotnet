// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

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

	// Keyed by property rather than by column layout: a type read under several column schemas would otherwise compile
	// every setter and build every converter contract once per schema.
	private readonly ConcurrentDictionary<JsonPropertyInfo, Delegate?> _compiledSetterCache = [];
	private readonly ConcurrentDictionary<JsonPropertyInfo, JsonTypeInfo?> _propertyContractCache = [];

	/// <summary>The underlying <see cref="JsonSerializerOptions"/>.</summary>
	public JsonSerializerOptions Options => options;

	/// <summary>
	/// Whether no part of the options' resolver can carry a contract modifier, so a setter compiled for a member assigns
	/// exactly what <see cref="JsonPropertyInfo.Set"/> would.
	/// </summary>
	/// <remarks>
	/// The resolver chain exposes the parts of a combined resolver, so each part is judged on its own, the combination the
	/// library builds from a context included. An empty chain counts as modifier-free because the serializer fills in its
	/// own reflection resolver without adding it to the chain. The answer is read at each layout build: a part that has
	/// not resolved a type yet can still gain a modifier after the options are read-only, while a part that resolved the
	/// build's type is locked by then.
	/// </remarks>
	public bool ResolverIsModifierFree => options.TypeInfoResolverChain.All(IsModifierFreeResolver);

	/// <summary>
	/// Whether <paramref name="resolver"/> provably carries no contract modifier: a source-generated
	/// <see cref="JsonSerializerContext"/>, or a <see cref="DefaultJsonTypeInfoResolver"/> without modifiers.
	/// </summary>
	/// <remarks>
	/// <see cref="JsonSerializableAttribute"/> declared on the context's own type marks a source-generated context, whose
	/// contracts carry no replaced setter. A hand-written context, a subclass of a generated one included, can replace
	/// <see cref="JsonPropertyInfo.Set"/> in its own <c>GetTypeInfo</c>, and so can a subclass of the default resolver
	/// without a modifier entry, so neither counts.
	/// </remarks>
	private static bool IsModifierFreeResolver(IJsonTypeInfoResolver resolver) =>
		resolver switch
		{
			JsonSerializerContext context => context.GetType().IsDefined(typeof(JsonSerializableAttribute), inherit: false),
			DefaultJsonTypeInfoResolver { Modifiers.Count: 0 } => resolver.GetType() == typeof(DefaultJsonTypeInfoResolver),
			_ => false
		};

	/// <summary>Returns the contract <paramref name="options"/> resolve for <paramref name="type"/>, or null when the type has no usable metadata.</summary>
	[UnconditionalSuppressMessage(
		"AOT",
		"IL3050",
		Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext."
	)]
	[UnconditionalSuppressMessage(
		"Trimming",
		"IL2026",
		Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext."
	)]
	internal static JsonTypeInfo? TryResolveTypeInfo(Type type, JsonSerializerOptions options)
	{
		try
		{
			return options.GetTypeInfo(type);
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// The documented STJ failures for a type without usable metadata. Anything else is a resolver bug and must surface.
			return null;
		}
	}

	/// <summary>
	/// Whether <paramref name="converter"/> comes from the serializer's own assembly, so it reads the token shapes its
	/// contract kind describes.
	/// </summary>
	internal static bool IsBuiltInConverter(JsonConverter converter) => converter.GetType().Assembly == typeof(JsonSerializerOptions).Assembly;

	/// <summary>
	/// Returns the converter that reads <paramref name="type"/> when a member names <paramref name="converter"/>. Reflection
	/// metadata keeps a property's factory in <see cref="JsonPropertyInfo.CustomConverter"/>, so the converter that reads the
	/// cell is the one the factory creates. A factory that creates no converter gives null, and a factory that cannot
	/// convert <paramref name="type"/> comes back unchanged.
	/// </summary>
	internal static JsonConverter? ExpandConverter(JsonConverter converter, Type type, JsonSerializerOptions options) =>
		converter is JsonConverterFactory factory && factory.CanConvert(type) ? factory.CreateConverter(type, options) : converter;

	/// <summary>
	/// Returns the <see cref="JsonTypeInfo"/> for <paramref name="type"/>, validating that the
	/// type uses property-based serialization. Types with a type-level <see cref="JsonConverterAttribute"/>
	/// have <see cref="JsonTypeInfoKind.None"/> and cannot be used as ES|QL entity types.
	/// </summary>
	public JsonTypeInfo GetPropertyBasedTypeInfo(Type type) =>
		Cached(_typeInfoCache, type, t =>
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
	/// Returns all JSON property names for a type, cached once the options are read-only.
	/// </summary>
	public HashSet<string> GetAllPropertyNames(Type type) =>
		Cached(_propertyNamesCache, type, t =>
		{
			var typeInfo = GetPropertyBasedTypeInfo(t);
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var prop in typeInfo.Properties)
				_ = names.Add(prop.Name);

			return names;
		});

	/// <summary>
	/// Returns a case-insensitive CLR-member-name to <see cref="JsonPropertyInfo"/> map for
	/// <paramref name="type"/>, cached once the options are read-only. Used to resolve constructor
	/// parameter names to their corresponding JSON property metadata.
	/// </summary>
	public Dictionary<string, JsonPropertyInfo> GetConstructorPropertyMap(Type type) =>
		Cached(_ctorPropertyMapCache, type, t =>
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
	/// inserted at highest priority. Results are cached by converter runtime type once the options are read-only.
	/// </summary>
	public JsonSerializerOptions GetOptionsWithConverter(JsonConverter converter) =>
		Cached(_converterOptionsCache, converter.GetType(), _ =>
		{
			var result = new JsonSerializerOptions(options);
			result.Converters.Insert(0, converter);
			return result;
		});

	/// <summary>Returns the typed setter compiled for <paramref name="property"/>, or null where none can be compiled.</summary>
	public Delegate? GetOrCompileSetter(JsonPropertyInfo property, DirectBinderKind kind) =>
		Cached(_compiledSetterCache, property, p => DirectSetterCompiler.TryCreate(p, kind));

	/// <summary>Returns the contract that reads <paramref name="property"/> through <paramref name="converter"/>, or null when none can be built.</summary>
	public JsonTypeInfo? GetOrCreatePropertyContract(JsonPropertyInfo property, JsonConverter converter) =>
		Cached(_propertyContractCache, property, p => PropertyConverterContracts.TryCreate(p.PropertyType, converter, options));

	private Dictionary<MemberInfo, JsonPropertyInfo> GetMemberPropertyMap(Type type) =>
		Cached(_memberPropertyMapCache, type, t =>
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

	// The serializer resolves a fresh contract on every lookup while the options are mutable and shares one per type once
	// they are read-only, so a contract kept from before the freeze is one the serializer never uses again.
	private TValue Cached<TKey, TValue>(ConcurrentDictionary<TKey, TValue> cache, TKey key, Func<TKey, TValue> factory) where TKey : notnull =>
		options.IsReadOnly ? cache.GetOrAdd(key, factory) : factory(key);
}
