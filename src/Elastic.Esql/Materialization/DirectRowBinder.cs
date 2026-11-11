// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;

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

	/// <summary>Cell deserialized through its own contract (enums, dates without a built-in kind, collections, dictionaries, nested objects).</summary>
	Converter
}

/// <summary>
/// Pre-computed per-column binding metadata for flat column layouts. Enables reading each cell directly off the
/// <see cref="Utf8JsonReader"/> and assigning it through a compiled typed setter, or through
/// <see cref="JsonPropertyInfo.Set"/> where no setter could be compiled.
/// </summary>
/// <remarks>
/// Binding directly is a large net win over the assemble-and-deserialize pipeline, which tokenizes, re-writes and
/// re-parses every cell. Built once per (target type, column schema) and cached on the <see cref="ColumnLayout"/>.
/// It is immutable after construction because layouts are shared across threads.
/// </remarks>
internal sealed class DirectRowBinder
{
	public required DirectBinderKind[] Kinds { get; init; }
	public required JsonPropertyInfo[] Properties { get; init; }

	/// <summary>
	/// Per-column <c>Action&lt;object, TValue&gt;</c> compiled for the exact cell type, or null where the column
	/// assigns through the boxing <see cref="JsonPropertyInfo.Set"/> (Native AOT, resolvers that can carry a contract
	/// modifier, struct targets, unresolvable members).
	/// </summary>
	public required Delegate?[] TypedSetters { get; init; }

	/// <summary>Contract used to deserialize a <see cref="DirectBinderKind.Converter"/> cell, or null for the built-in kinds.</summary>
	public required JsonTypeInfo?[] CellTypeInfos { get; init; }

	/// <summary>
	/// Per column, whether the target property is a collection. A bare scalar cell for such a column is wrapped in
	/// brackets before its contract reads it, matching the row path's <c>[value]</c> rewrite.
	/// </summary>
	public required bool[] IsCollection { get; init; }

	public required Func<object> CreateObject { get; init; }

	/// <summary>Number of <see cref="DirectBinderKind.Converter"/> columns. A row with any validates every cell before it deserializes one.</summary>
	public required int ConverterCellCount { get; init; }

	/// <summary>Depth limit for a cell read on its own reader: the options' limit less the level the assembled row object adds.</summary>
	public required int CellMaxDepth { get; init; }

	/// <summary>
	/// Builds a binder for a flat layout, or returns null when the type itself cannot be bound with exact serializer
	/// fidelity: parameterized constructors, serialization callbacks, extension data, number handling on the type or
	/// a property, a property-level converter no cell contract can be built for, populate-style object creation, a
	/// required member without a column, two columns for one property, a converter cell under a depth limit of one, or
	/// a converter cell under options that read reference metadata.
	/// Cells that are not one of the built-in scalar kinds deserialize through their own contract instead.
	/// </summary>
	public static DirectRowBinder? TryCreate(ColumnNode[] leafNodes, JsonTypeInfo typeInfo, JsonMetadataManager metadata)
	{
		var options = metadata.Options;

		if (typeInfo.Kind != JsonTypeInfoKind.Object || typeInfo.CreateObject is null)
			return null;

		// Types with serialization callbacks must stay on the slow path: the fast path may create (and discard)
		// an instance on a fallback or incomplete-retry row, which would invoke OnDeserializing more than once.
		if (typeInfo.OnDeserializing is not null || typeInfo.OnDeserialized is not null)
			return null;

		// A type-level [JsonNumberHandling] applies to every value the serializer reads into the type, but a converter
		// cell of collection or dictionary type is deserialized with a fresh read stack that never sees it, so its
		// elements would parse under the options' handling instead.
		if (typeInfo.NumberHandling is not null)
			return null;

		var count = leafNodes.Length;
		var kinds = new DirectBinderKind[count];
		var properties = new JsonPropertyInfo[count];
		var typedSetters = new Delegate?[count];
		var cellTypeInfos = new JsonTypeInfo?[count];
		var isCollection = new bool[count];
		var converterCellCount = 0;
		var bound = new HashSet<JsonPropertyInfo>();

		for (var i = 0; i < count; i++)
		{
			var property = FindProperty(typeInfo, leafNodes[i].PropertyName, options.PropertyNameCaseInsensitive);
			if (property is null || property.Set is null || property.IsExtensionData || property.NumberHandling is not null)
				return null;

			// Two columns for one property, or populate-style creation, are the serializer's business.
			if (!bound.Add(property) || EffectiveCreationHandling(property, typeInfo, options) == JsonObjectCreationHandling.Populate)
				return null;

			properties[i] = property;
			isCollection[i] = leafNodes[i].IsCollection;

			// Checked before the built-in kinds, which would read the cell without the property's converter.
			if (property.CustomConverter is null && TryClassifyScalar(property.PropertyType, options, out var kind))
			{
				kinds[i] = kind;
				// A compiled setter writes the member directly and would skip a modifier's replacement Set.
				typedSetters[i] = metadata.ResolverIsModifierFree ? metadata.GetOrCompileSetter(property, kind) : null;
				continue;
			}

			var cellTypeInfo = property.CustomConverter is { } customConverter
				? metadata.GetOrCreatePropertyContract(property, customConverter)
				: JsonMetadataManager.TryResolveTypeInfo(property.PropertyType, options);
			if (cellTypeInfo is null)
				return null;

			kinds[i] = DirectBinderKind.Converter;
			cellTypeInfos[i] = cellTypeInfo;
			converterCellCount++;
		}

		// A required property without a column fails every row in the serializer, so keep that behavior on the slow path.
		foreach (var property in typeInfo.Properties)
		{
			if (property.IsRequired && !bound.Contains(property))
				return null;
		}

		var effectiveMaxDepth = ColumnLayout.EffectiveMaxDepth(options);

		// A cell reader would get a depth limit of zero, which it reads as the default, so it could not reject a nested
		// converter cell the way the serializer rejects the assembled row. The assemble path serves these rows instead.
		if (converterCellCount > 0 && effectiveMaxDepth < 2)
			return null;

		// Each converter cell deserializes in a call of its own, so an $id or $ref that spans cells resolves differently
		// than in the serializer's single call over the row. IgnoreCycles reads no metadata and keeps the direct path.
		if (converterCellCount > 0 && options.ReferenceHandler is { } handler && handler != ReferenceHandler.IgnoreCycles)
			return null;

		return new DirectRowBinder
		{
			Kinds = kinds,
			Properties = properties,
			TypedSetters = typedSetters,
			CellTypeInfos = cellTypeInfos,
			IsCollection = isCollection,
			CreateObject = typeInfo.CreateObject,
			ConverterCellCount = converterCellCount,
			CellMaxDepth = effectiveMaxDepth - 1
		};
	}

	/// <summary>Resolves the creation handling the serializer would apply to a property: property, then type, then options.</summary>
	private static JsonObjectCreationHandling EffectiveCreationHandling(JsonPropertyInfo property, JsonTypeInfo typeInfo, JsonSerializerOptions options) =>
		property.ObjectCreationHandling ?? typeInfo.PreferredPropertyObjectCreationHandling ?? options.PreferredObjectCreationHandling;

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

	/// <summary>Classifies a flat column's property type or a scalar read target as a built-in kind read through the stock converter.</summary>
	internal static bool TryClassifyScalar(Type type, JsonSerializerOptions options, out DirectBinderKind kind) =>
		TryClassify(type, out kind) && UsesBuiltInConverter(type, options);

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
			if (!options.TryGetTypeInfo(propertyType, out var propertyTypeInfo) || !HasBuiltInConverter(propertyTypeInfo))
				return false;

			// For nullable value types the wrapper converter is always a built-in NullableConverter<T>.
			// Also check the underlying type so a user converter for T disqualifies T? as well.
			var underlying = Nullable.GetUnderlyingType(propertyType);
			return underlying is null || (options.TryGetTypeInfo(underlying, out var underlyingTypeInfo) && HasBuiltInConverter(underlyingTypeInfo));
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// The documented STJ failures for an unsupported type: keep the slow path. Anything
			// else is a resolver bug and must surface.
			return false;
		}
	}

	/// <summary>Whether <paramref name="typeInfo"/> reads through one of the serializer's own converters.</summary>
	internal static bool HasBuiltInConverter(JsonTypeInfo typeInfo) => JsonMetadataManager.IsBuiltInConverter(typeInfo.Converter);
}
