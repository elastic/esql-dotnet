// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

/// <summary>Per-column value classification for the direct-binding fast path.</summary>
internal enum DirectBinderKind
{
	String,
	Bool,
	Int32,
	Int64,
	Double,
	Single,
	Decimal,
	DateTime,
	DateTimeOffset,
	Guid,

	/// <summary>Cell deserialized through its own contract (enums, dates without a built-in kind, collections, dictionaries, property converters).</summary>
	Converter
}

/// <summary>
/// Pre-computed per-column binding metadata for flat column layouts. Enables reading each cell directly off the
/// <see cref="Utf8JsonReader"/> and assigning it through a compiled typed setter, or through
/// <see cref="JsonPropertyInfo.Set"/> where no setter could be compiled.
/// </summary>
/// <remarks>
/// Binding directly is a large net win over the assemble-and-deserialize pipeline, which tokenizes, re-writes and
/// re-parses every cell. Built once per (target type, column schema) and cached on the <see cref="ColumnLayout"/>;
/// immutable after construction because layouts are shared across threads.
/// </remarks>
internal sealed class DirectRowBinder
{
	public required DirectBinderKind[] Kinds { get; init; }
	public required JsonPropertyInfo[] Properties { get; init; }

	/// <summary>
	/// Per-column <c>Action&lt;object, TValue&gt;</c> compiled for the exact cell type, or null where the column
	/// assigns through the boxing <see cref="JsonPropertyInfo.Set"/> (Native AOT, struct targets, unresolvable members).
	/// </summary>
	public required Delegate?[] TypedSetters { get; init; }

	/// <summary>Contract used to deserialize a <see cref="DirectBinderKind.Converter"/> cell; null for the built-in kinds.</summary>
	public required JsonTypeInfo?[] CellTypeInfos { get; init; }

	/// <summary>
	/// Element contract for collection-typed converter cells whose contract creates an <see cref="System.Collections.IList"/>;
	/// a bare scalar cell is deserialized as one element and wrapped, matching the row path's <c>[value]</c> rewrite.
	/// </summary>
	public required JsonTypeInfo?[] ElementTypeInfos { get; init; }

	/// <summary>Per column, whether the property is <c>required</c>: a null cell then falls back so the serializer raises its error.</summary>
	public required bool[] IsRequired { get; init; }

	public required Func<object> CreateObject { get; init; }

	/// <summary>
	/// Builds a binder for a flat layout, or returns null when the type itself cannot be bound with exact serializer
	/// fidelity: parameterized constructors, serialization callbacks, extension data, per-property number handling,
	/// populate-style object creation, a required member without a column, or two columns for one property.
	/// Cells that are not one of the built-in scalar kinds deserialize through their own contract instead.
	/// </summary>
	public static DirectRowBinder? TryCreate(ColumnNode[] leafNodes, JsonTypeInfo typeInfo, JsonSerializerOptions options)
	{
		if (typeInfo.Kind != JsonTypeInfoKind.Object || typeInfo.CreateObject is null)
			return null;

		// Types with serialization callbacks must stay on the slow path: the fast path may create (and discard)
		// an instance on a fallback or incomplete-retry row, which would invoke OnDeserializing more than once.
		if (typeInfo.OnDeserializing is not null || typeInfo.OnDeserialized is not null)
			return null;

		var count = leafNodes.Length;
		var kinds = new DirectBinderKind[count];
		var properties = new JsonPropertyInfo[count];
		var typedSetters = new Delegate?[count];
		var cellTypeInfos = new JsonTypeInfo?[count];
		var elementTypeInfos = new JsonTypeInfo?[count];
		var isRequired = new bool[count];
		var bound = new HashSet<JsonPropertyInfo>();

		for (var i = 0; i < count; i++)
		{
			var property = FindProperty(typeInfo, leafNodes[i].PropertyName, options.PropertyNameCaseInsensitive);
			if (property is null || property.Set is null || property.IsExtensionData || property.NumberHandling is not null)
				return null;

			// Two columns for one property, or populate-style creation, are the serializer's business.
			if (!bound.Add(property) || (property.ObjectCreationHandling ?? options.PreferredObjectCreationHandling) == JsonObjectCreationHandling.Populate)
				return null;

			properties[i] = property;
			isRequired[i] = property.IsRequired;

			if (property.CustomConverter is null && TryClassify(property.PropertyType, out var kind) && UsesBuiltInConverter(property.PropertyType, options))
			{
				kinds[i] = kind;
				typedSetters[i] = DirectSetterCompiler.TryCreate(property, kind, typeInfo.Type);
				continue;
			}

			var cellTypeInfo = ResolveCellTypeInfo(property, options);
			if (cellTypeInfo is null)
				return null;

			kinds[i] = DirectBinderKind.Converter;
			cellTypeInfos[i] = cellTypeInfo;
			elementTypeInfos[i] = leafNodes[i].IsCollection ? ResolveListElementTypeInfo(cellTypeInfo) : null;
		}

		// A required property without a column fails every row in the serializer; keep that behavior on the slow path.
		foreach (var property in typeInfo.Properties)
		{
			if (property.IsRequired && !bound.Contains(property))
				return null;
		}

		return new DirectRowBinder
		{
			Kinds = kinds,
			Properties = properties,
			TypedSetters = typedSetters,
			CellTypeInfos = cellTypeInfos,
			ElementTypeInfos = elementTypeInfos,
			IsRequired = isRequired,
			CreateObject = typeInfo.CreateObject
		};
	}

	/// <summary>Resolves the contract a converter cell deserializes through, or null when the type has none usable.</summary>
	private static JsonTypeInfo? ResolveCellTypeInfo(JsonPropertyInfo property, JsonSerializerOptions options)
	{
		try
		{
			if (property.CustomConverter is not { } converter)
				return options.GetTypeInfo(property.PropertyType);

			// A property-level converter is not part of the property type's contract; a private options copy
			// carrying it at highest priority yields a contract that applies it exactly as the serializer would.
			// Frozen before use so the contract is configured and cached once instead of per resolution.
			var withConverter = new JsonSerializerOptions(options);
			withConverter.Converters.Insert(0, converter);
			withConverter.MakeReadOnly();
			return withConverter.GetTypeInfo(property.PropertyType);
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// The documented STJ failures for an unsupported type: keep the slow path.
			return null;
		}
	}

	/// <summary>Resolves the element contract a single-valued multi-value cell is deserialized and wrapped with.</summary>
	private static JsonTypeInfo? ResolveListElementTypeInfo(JsonTypeInfo collectionTypeInfo)
	{
		if (collectionTypeInfo.Kind != JsonTypeInfoKind.Enumerable || collectionTypeInfo.ElementType is null || collectionTypeInfo.CreateObject is null)
			return null;

		// Only list-shaped contracts can take a wrapped single value; arrays and sets fall back per row.
		if (collectionTypeInfo.CreateObject() is not IList)
			return null;

		try
		{
			return collectionTypeInfo.Options.GetTypeInfo(collectionTypeInfo.ElementType);
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			return null;
		}
	}

	private static JsonPropertyInfo? FindProperty(JsonTypeInfo typeInfo, string jsonName, bool caseInsensitive)
	{
		foreach (var property in typeInfo.Properties)
		{
			if (string.Equals(property.Name, jsonName, StringComparison.Ordinal))
				return property;
		}

		if (!caseInsensitive)
			return null;

		JsonPropertyInfo? match = null;
		foreach (var property in typeInfo.Properties)
		{
			if (!string.Equals(property.Name, jsonName, StringComparison.OrdinalIgnoreCase))
				continue;

			// Ambiguous case-insensitive matches are left to the serializer's own resolution.
			if (match is not null)
				return null;

			match = property;
		}

		return match;
	}

	/// <summary>Classifies a scalar read target the same way a flat column is classified, including the converter check.</summary>
	internal static bool TryClassifyScalar(Type type, JsonTypeInfo? typeInfo, JsonSerializerOptions options, out DirectBinderKind kind)
	{
		kind = default;
		return typeInfo is not null && TryClassify(type, out kind) && UsesBuiltInConverter(type, options);
	}

	private static bool TryClassify(Type propertyType, out DirectBinderKind kind)
	{
		var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

		// Enums share integer type codes but need converter-driven handling.
		if (type.IsEnum)
		{
			kind = default;
			return false;
		}

		var classified = Type.GetTypeCode(type) switch
		{
			TypeCode.String => DirectBinderKind.String,
			TypeCode.Boolean => DirectBinderKind.Bool,
			TypeCode.Int32 => DirectBinderKind.Int32,
			TypeCode.Int64 => DirectBinderKind.Int64,
			TypeCode.Double => DirectBinderKind.Double,
			TypeCode.Single => DirectBinderKind.Single,
			TypeCode.Decimal => DirectBinderKind.Decimal,
			TypeCode.DateTime => DirectBinderKind.DateTime,
			TypeCode.Object when type == typeof(DateTimeOffset) => DirectBinderKind.DateTimeOffset,
			TypeCode.Object when type == typeof(Guid) => DirectBinderKind.Guid,
			_ => (DirectBinderKind?)null
		};

		kind = classified ?? default;
		return classified is not null;
	}

	/// <summary>
	/// The fast path replicates only the stock converters for the supported scalar kinds; a
	/// converter resolved from any other assembly (user converters registered on the options or
	/// via the resolver) disqualifies the layout.
	/// </summary>
	private static bool UsesBuiltInConverter(Type propertyType, JsonSerializerOptions options)
	{
		try
		{
			if (!options.TryGetTypeInfo(propertyType, out var propertyTypeInfo)
				|| propertyTypeInfo.Converter.GetType().Assembly != typeof(JsonSerializerOptions).Assembly)
				return false;

			// For nullable value types the wrapper converter is always a built-in NullableConverter<T>;
			// also check the underlying type so a user converter for T disqualifies T? as well.
			var underlying = Nullable.GetUnderlyingType(propertyType);
			if (underlying is null)
				return true;

			return options.TryGetTypeInfo(underlying, out var underlyingTypeInfo)
				&& underlyingTypeInfo.Converter.GetType().Assembly == typeof(JsonSerializerOptions).Assembly;
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// The documented STJ failures for an unsupported type: keep the slow path. Anything
			// else is a resolver bug and must surface.
			return false;
		}
	}
}
