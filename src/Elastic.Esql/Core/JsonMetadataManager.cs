// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.CodeDom.Compiler;
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
	private readonly ConcurrentDictionary<Type, HashSet<string>> _propertyNamesCache = [];

	// Keyed by member with the converter resolved in the factory, so the cache never holds a converter resolved before the
	// options froze, and two members whose converters share a runtime type get their own copies.
	private readonly ConcurrentDictionary<MemberInfo, JsonSerializerOptions?> _converterOptionsCache = [];

	private readonly ConcurrentDictionary<Type, Dictionary<string, JsonPropertyInfo>> _ctorPropertyMapCache = [];
	private readonly ConcurrentDictionary<Type, Dictionary<MemberInfo, JsonPropertyInfo>> _memberPropertyMapCache = [];

	// Keyed by property rather than by column layout: a type read under several column schemas would otherwise compile
	// every setter and build every converter contract once per schema.
	private readonly ConcurrentDictionary<JsonPropertyInfo, Delegate?> _compiledSetterCache = [];
	private readonly ConcurrentDictionary<JsonPropertyInfo, JsonTypeInfo?> _propertyContractCache = [];

	// The serializer keeps its chain type internal, and Combine with no resolvers returns an instance of it where a single
	// resolver would come back unwrapped.
	private static readonly Type CombinedChainType = JsonTypeInfoResolver.Combine().GetType();

	/// <summary>The underlying <see cref="JsonSerializerOptions"/>.</summary>
	public JsonSerializerOptions Options => options;

	/// <summary>
	/// Whether no part of the options' resolver can carry a contract modifier, so no property carries a type info the
	/// resolver supplied or a replaced <see cref="JsonPropertyInfo.Set"/>, and a row bound directly reads and assigns its
	/// cells as the serializer does.
	/// </summary>
	/// <remarks>
	/// The resolver chain exposes the parts of a combined resolver assigned to the options, so each part is judged on
	/// its own, the combination the library builds from a context included. An empty chain counts as modifier-free
	/// because the serializer fills in its own reflection resolver without adding it to the chain. The answer is read
	/// at each layout build: a part that has not resolved a type yet can still gain a modifier after the options are
	/// read-only, while a part that resolved the build's type is locked by then.
	/// </remarks>
	public bool ResolverIsModifierFree => options.TypeInfoResolverChain.All(IsModifierFreeResolver);

	/// <summary>
	/// Whether <paramref name="resolver"/> provably carries no contract modifier: a source-generated
	/// <see cref="JsonSerializerContext"/>, a <see cref="DefaultJsonTypeInfoResolver"/> without modifiers, or the
	/// serializer's own combination of such resolvers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A source-generated context's contracts carry no replaced setter and supply no property type info. Its own type
	/// declares both <see cref="JsonSerializableAttribute"/> and the <see cref="GeneratedCodeAttribute"/> marker of the
	/// source generator, and both are required: a hand-written class can carry the serializable attribute, and only the
	/// generator writes the marker. A hand-written context, a subclass of a generated one included, can replace
	/// <see cref="JsonPropertyInfo.Set"/> or supply a property's type info in its own <c>GetTypeInfo</c>, and so can a
	/// subclass of the default resolver without a modifier entry, so neither counts.
	/// </para>
	/// <para>
	/// A combined resolver added to the options' chain, rather than assigned, stays one entry of it. An entry of the
	/// exact chain type <see cref="JsonTypeInfoResolver.Combine(IJsonTypeInfoResolver[])"/> builds is judged by its
	/// parts, because it is read-only from its creation and only dispatches to its parts in order. The exact type is
	/// required because the bound chain of another options object is a subclass of it that stays mutable until its own
	/// options freeze, so its parts can change after these options cached a contract through them.
	/// </para>
	/// </remarks>
	private static bool IsModifierFreeResolver(IJsonTypeInfoResolver resolver) =>
		resolver switch
		{
			JsonSerializerContext context =>
				context.GetType().IsDefined(typeof(JsonSerializableAttribute), inherit: false)
				&& context.GetType().GetCustomAttribute<GeneratedCodeAttribute>(inherit: false) is { Tool: "System.Text.Json.SourceGeneration" },
			DefaultJsonTypeInfoResolver { Modifiers.Count: 0 } => resolver.GetType() == typeof(DefaultJsonTypeInfoResolver),
			IList<IJsonTypeInfoResolver> parts when resolver.GetType() == CombinedChainType => parts.All(IsModifierFreeResolver),
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
	public JsonTypeInfo GetPropertyBasedTypeInfo(Type type)
	{
		var typeInfo = options.GetTypeInfo(type);

		if (typeInfo.Kind == JsonTypeInfoKind.None)
			throw new NotSupportedException(
				$"Type '{type.Name}' uses a custom JsonConverter and cannot be used as an ES|QL entity type. " +
				"ES|QL requires property-based serialization.");

		return typeInfo;
	}

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
	/// Returns a copy of the options with the <see cref="JsonConverter"/> explicitly set on the property identified by
	/// <paramref name="member"/> inserted at highest priority, or <see langword="null"/> if none is configured. Falls back
	/// gracefully when the declaring type is not registered in the serializer context. The copy is cached by member once
	/// the options are read-only.
	/// </summary>
	public JsonSerializerOptions? GetOptionsWithPropertyConverter(MemberInfo? member)
	{
		if (member is null)
			return null;

		return Cached(_converterOptionsCache, member, m =>
		{
			try
			{
				if (m.DeclaringType is not { } declaringType
					|| !GetMemberPropertyMap(declaringType).TryGetValue(m, out var property)
					|| property.CustomConverter is not { } converter)
					return null;

				var result = new JsonSerializerOptions(options);
				result.Converters.Insert(0, converter);
				return result;
			}
			catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
			{
				// Declaring type not registered: no per-property converter can apply.
				return null;
			}
		});
	}

	/// <summary>Returns the typed setter compiled for <paramref name="property"/>, or null where none can be compiled.</summary>
	public Delegate? GetOrCompileSetter(JsonPropertyInfo property, DirectBinderKind kind) =>
		Cached(_compiledSetterCache, property, p => DirectSetterCompiler.TryCreate(p, kind));

	/// <summary>Returns the contract that reads <paramref name="property"/> through its custom converter, or null when none can be built.</summary>
	public JsonTypeInfo? GetOrCreatePropertyContract(JsonPropertyInfo property) =>
		Cached(
			_propertyContractCache,
			property,
			p => p.CustomConverter is { } converter ? PropertyConverterContracts.TryCreate(p.PropertyType, converter, options) : null
		);

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
