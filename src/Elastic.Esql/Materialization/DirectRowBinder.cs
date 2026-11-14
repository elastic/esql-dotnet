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
	Enum,
	StringList,
	StringArray,

	/// <summary>
	/// Cell deserialized through its own contract (enums other than a <see cref="DirectBinderKind.Enum"/> cell, dates
	/// without a built-in kind, collections other than a <see cref="StringList"/> or a <see cref="StringArray"/>,
	/// dictionaries, nested objects).
	/// </summary>
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
	// The reflection resolver and the source generator hand each of these types the converter JsonMetadataServices exposes.
	// Another converter of the serializer's assembly, such as the object converter a factory can return for string, reads
	// other tokens than the inline read accepts, so the assembly alone proves nothing.
	private static readonly Dictionary<Type, Type> StockConverterTypes = new()
	{
		[typeof(bool)] = JsonMetadataServices.BooleanConverter.GetType(),
		[typeof(DateTime)] = JsonMetadataServices.DateTimeConverter.GetType(),
		[typeof(DateTimeOffset)] = JsonMetadataServices.DateTimeOffsetConverter.GetType(),
		[typeof(decimal)] = JsonMetadataServices.DecimalConverter.GetType(),
		[typeof(double)] = JsonMetadataServices.DoubleConverter.GetType(),
		[typeof(float)] = JsonMetadataServices.SingleConverter.GetType(),
		[typeof(Guid)] = JsonMetadataServices.GuidConverter.GetType(),
		[typeof(int)] = JsonMetadataServices.Int32Converter.GetType(),
		[typeof(long)] = JsonMetadataServices.Int64Converter.GetType(),
		[typeof(string)] = JsonMetadataServices.StringConverter.GetType()
	};

	// The serializer keeps its nullable wrapper type internal, and GetNullableConverter creates the wrapper the resolvers
	// create, so a sample's generic definition names it for every underlying type.
	private static readonly Type NullableConverterDefinition = JsonMetadataServices
		.GetNullableConverter(JsonMetadataServices.CreateValueInfo<int>(new JsonSerializerOptions(), JsonMetadataServices.Int32Converter))
		.GetType()
		.GetGenericTypeDefinition();

	public required DirectBinderKind[] Kinds { get; init; }
	public required JsonPropertyInfo[] Properties { get; init; }

	/// <summary>
	/// Per-column <c>Action&lt;object, TValue&gt;</c> compiled for the exact cell type, which is <c>long</c> for the bits of
	/// an enum cell, or null where the column assigns through the boxing <see cref="JsonPropertyInfo.Set"/> (Native AOT,
	/// struct targets, unresolvable members). A <see cref="DirectBinderKind.Converter"/> column keeps its setter in
	/// <see cref="ConverterCells"/>.
	/// </summary>
	public required Delegate?[] TypedSetters { get; init; }

	/// <summary>
	/// The list contract that creates the list of a <see cref="DirectBinderKind.StringList"/> cell, or null for every other
	/// kind. A <see cref="DirectBinderKind.Converter"/> cell holds its own contract, and a
	/// <see cref="DirectBinderKind.StringArray"/> cell collects its elements in the row scratch.
	/// </summary>
	public required JsonTypeInfo?[] CellTypeInfos { get; init; }

	/// <summary>The cell that reads and assigns a <see cref="DirectBinderKind.Converter"/> column, or null for every other kind.</summary>
	public required ConverterCell?[] ConverterCells { get; init; }

	/// <summary>
	/// The type code of a <see cref="DirectBinderKind.Enum"/> column's enum, which is the code of its underlying integer
	/// type, or <see cref="TypeCode.Empty"/> for every other kind.
	/// </summary>
	public required TypeCode[] EnumTypeCodes { get; init; }

	/// <summary>
	/// The enum type of a <see cref="DirectBinderKind.Enum"/> column without its nullable wrapper, which boxes the bits
	/// where the column has no typed setter, or null for every other kind.
	/// </summary>
	public required Type?[] EnumTypes { get; init; }

	/// <summary>
	/// Per column, whether the target property is a collection. A bare scalar cell for such a column is wrapped in
	/// brackets before its contract reads it, matching the row path's <c>[value]</c> rewrite, and a string list cell
	/// reads a bare string as its only element.
	/// </summary>
	public required bool[] IsCollection { get; init; }

	public required Func<object> CreateObject { get; init; }

	/// <summary>
	/// Number of <see cref="DirectBinderKind.Converter"/>, <see cref="DirectBinderKind.Enum"/>,
	/// <see cref="DirectBinderKind.StringArray"/> and <see cref="DirectBinderKind.StringList"/> columns. A row with any
	/// validates every cell before it creates the instance, decodes a string list or deserializes a converter cell.
	/// </summary>
	public required int ValidatedCellCount { get; init; }

	/// <summary>Depth limit for a cell read on its own reader: the options' limit less the level the assembled row object adds.</summary>
	public required int CellMaxDepth { get; init; }

	/// <summary>
	/// Builds a binder for a flat layout, or returns null when a part of the options' resolver chain can supply property
	/// metadata the binder cannot see, or when the type itself cannot be bound with exact serializer fidelity:
	/// parameterized constructors, serialization callbacks, extension data, number handling on the type or a property, a
	/// property-level converter no cell contract can be built for, populate-style object creation, a required member
	/// without a column, two columns for one property, a mapped property whose JSON name starts with $ under options that
	/// read reference metadata or on a type with polymorphism options, a converter cell or a string list cell under a depth
	/// limit of one, or either under options that read reference metadata.
	/// A <c>List&lt;string&gt;</c> or <c>string[]</c> cell that the serializer reads with its own collection converter and
	/// its stock converter for string is read as a string list, an enum cell on a numeric column that the serializer reads
	/// with its own converters, which accept numbers, is read as an enum, the nullable form only under options with no
	/// user-written converter factory and no converter for the nullable type, and any other cell that is not one of the
	/// built-in scalar kinds deserializes through its own contract. <paramref name="columns"/> holds the ES|QL type of the
	/// column each leaf node reads.
	/// </summary>
	public static DirectRowBinder? TryCreate(
		ColumnNode[] leafNodes,
		ReadOnlySpan<EsqlResponseReader.ColumnInfo> columns,
		JsonTypeInfo typeInfo,
		JsonMetadataManager metadata)
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

		// A scalar cell reads from the options' contract for its type and a converter cell through the contract the options
		// resolve for the property's type, while the serializer reads a property through any type info the resolver supplied
		// for it, which no public member of JsonPropertyInfo exposes. Only a chain of source-generated contexts and plain
		// default resolvers without modifiers is known to supply none.
		if (!metadata.ResolverIsModifierFree)
			return null;

		var count = leafNodes.Length;
		var kinds = new DirectBinderKind[count];
		var properties = new JsonPropertyInfo[count];
		var typedSetters = new Delegate?[count];
		var cellTypeInfos = new JsonTypeInfo?[count];
		var converterCells = new ConverterCell?[count];
		var enumTypeCodes = new TypeCode[count];
		var enumTypes = new Type?[count];
		var isCollection = new bool[count];
		var validatedCellCount = 0;
		var bound = new HashSet<JsonPropertyInfo>();
		var effectiveMaxDepth = ColumnLayout.EffectiveMaxDepth(options);
		var readsReferenceMetadata = options.ReferenceHandler is { } handler && handler != ReferenceHandler.IgnoreCycles;

		// Converter cells and string list cells bind only under these conditions. Under a depth limit of one, a cell reader
		// would get a depth limit of zero, which it reads as the default, so it could not reject a nested converter cell the
		// way the serializer rejects the assembled row, and the serializer rejects a string list cell's array there too.
		// The assemble path serves these rows instead.
		// Each converter cell deserializes in a call of its own, so an $id or $ref that spans cells resolves differently
		// than in the serializer's single call over the row. IgnoreCycles reads no metadata and keeps the direct path.
		var validatedCellsBind = effectiveMaxDepth >= 2 && !readsReferenceMetadata;

		for (var i = 0; i < count; i++)
		{
			var property = FindProperty(typeInfo, leafNodes[i].PropertyName, options.PropertyNameCaseInsensitive);
			if (property is null || property.Set is null || property.IsExtensionData || property.NumberHandling is not null)
				return null;

			// The serializer takes a property name with a leading $ for metadata under a handler that reads reference metadata
			// and on a type whose derived types carry a type discriminator. Under such a handler it reads $id and $ref as
			// reference metadata, which must hold a string, and rejects any other such name, and on such a type it rejects
			// every such name, so a row with such a column never binds through it, whatever the cell's kind. The check takes
			// every type with polymorphism options, so a type whose derived types carry no discriminator loses the direct
			// path for such a column and keeps its values.
			if ((readsReferenceMetadata || typeInfo.PolymorphismOptions is not null) && property.Name.Length > 0 && property.Name[0] == '$')
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
				// The modifier-free chain carries no replaced Set, so a setter compiled for the member assigns what Set would.
				typedSetters[i] = metadata.GetOrCompileSetter(property, kind);
				continue;
			}

			// An enum cell needs no cell reader and reads no reference metadata, so the converter cell conditions do not apply.
			if (TryClassifyEnum(property, columns[i].Type, options, out var enumType))
			{
				kinds[i] = DirectBinderKind.Enum;
				enumTypeCodes[i] = Type.GetTypeCode(enumType);
				enumTypes[i] = enumType;
				typedSetters[i] = metadata.GetOrCompileSetter(property, DirectBinderKind.Enum);
				validatedCellCount++;
				continue;
			}

			if (!validatedCellsBind)
				return null;

			if (property.CustomConverter is null && TryResolveStringListContract(property.PropertyType, options) is { } listTypeInfo)
			{
				kinds[i] = property.PropertyType == typeof(string[]) ? DirectBinderKind.StringArray : DirectBinderKind.StringList;
				if (kinds[i] == DirectBinderKind.StringList)
					cellTypeInfos[i] = listTypeInfo;

				typedSetters[i] = metadata.GetOrCompileSetter(property, kinds[i]);
				validatedCellCount++;
				continue;
			}

			var cellTypeInfo = property.CustomConverter is not null
				? metadata.GetOrCreatePropertyContract(property)
				: JsonMetadataManager.TryResolveTypeInfo(property.PropertyType, options);
			if (cellTypeInfo is null)
				return null;

			kinds[i] = DirectBinderKind.Converter;
			converterCells[i] = metadata.GetOrCreateConverterCell(property, cellTypeInfo);
			validatedCellCount++;
		}

		// A required property without a column fails every row in the serializer, so keep that behavior on the slow path.
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
			ConverterCells = converterCells,
			EnumTypeCodes = enumTypeCodes,
			EnumTypes = enumTypes,
			IsCollection = isCollection,
			CreateObject = typeInfo.CreateObject,
			ValidatedCellCount = validatedCellCount,
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

	/// <summary>
	/// Classifies an enum property, or the nullable form of one, on a numeric column as an enum cell when the serializer
	/// reads it with its own converters and those accept a number. The nullable form also needs options that hold no
	/// user-written converter factory and no converter for the nullable type. <paramref name="enumType"/> is the enum type
	/// without its nullable wrapper.
	/// </summary>
	private static bool TryClassifyEnum(JsonPropertyInfo property, string columnType, JsonSerializerOptions options, out Type enumType)
	{
		enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

		if (!enumType.IsEnum || columnType is not ("byte" or "integer" or "long" or "short" or "unsigned_long"))
			return false;

		// A property converter or a custom converter can hide its reading behind the serializer's nullable wrapper, which a
		// probe of 0 cannot see, so only the serializer's own converters qualify.
		if (property.CustomConverter is not null || !UsesBuiltInConverter(property.PropertyType, options))
			return false;

		if (JsonMetadataManager.TryResolveTypeInfo(property.PropertyType, options) is not { Kind: JsonTypeInfoKind.None } contract)
			return false;

		// A converter that rejects numbers, such as the string enum converter with integer values off, fails this read, and
		// the options are frozen by now, so the answer holds for the layout.
		try
		{
			_ = JsonSerializer.Deserialize("0"u8, contract);
		}
		catch (JsonException)
		{
			return false;
		}

		return true;
	}

	private static bool TryClassify(Type propertyType, out DirectBinderKind kind)
	{
		var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

		// Enums share integer type codes but need converter-driven handling, which an enum cell or a converter cell gives.
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
	/// Whether the options read <paramref name="propertyType"/> with the serializer's stock converter for it, and a nullable
	/// value type with the serializer's own nullable wrapper around the stock converter of the type under it.
	/// </summary>
	private static bool UsesBuiltInConverter(Type propertyType, JsonSerializerOptions options)
	{
		try
		{
			if (!options.TryGetTypeInfo(propertyType, out var propertyTypeInfo) || !IsStockConverter(propertyType, propertyTypeInfo.Converter))
				return false;

			var underlying = Nullable.GetUnderlyingType(propertyType);
			if (underlying is null)
				return true;

			// A converter or factory the serializer chose for the nullable type when it resolved the contract replaced the
			// default wrapper with its own product, which can be the serializer's wrapper around a custom converter, so the
			// wrapper's type tells nothing about what it holds. A converter names its target through Type, which
			// JsonConverter<T> seals, so reading it runs no user code. A user factory answers through CanConvert, whose
			// answer now need not be the one the serializer got, so its presence alone counts, while the serializer's own
			// factories keep a pure predicate. Under the modifier-free chain the options' converters are the one place such
			// a converter comes from.
			var mayReplaceWrapper = options.Converters.Any(
				converter => converter.Type == propertyType || (converter is JsonConverterFactory && !JsonMetadataManager.IsBuiltInConverter(converter))
			);
			if (mayReplaceWrapper)
				return false;

			// The default wrapper reads through the converter for the type under it, so a user converter for T disqualifies
			// T? as well.
			return options.TryGetTypeInfo(underlying, out var underlyingTypeInfo) && IsStockConverter(underlying, underlyingTypeInfo.Converter);
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// The documented STJ failures for an unsupported type: keep the slow path. Anything
			// else is a resolver bug and must surface.
			return false;
		}
	}

	private static bool IsStockConverter(Type type, JsonConverter converter)
	{
		// The stock enum converter is created per enum type, so any converter of the serializer's assembly qualifies and the
		// probe of 0 judges what it accepts.
		if (type.IsEnum)
			return JsonMetadataManager.IsBuiltInConverter(converter);

		var converterType = converter.GetType();
		if (Nullable.GetUnderlyingType(type) is not null)
			return converterType.IsGenericType && converterType.GetGenericTypeDefinition() == NullableConverterDefinition;

		return StockConverterTypes.TryGetValue(type, out var stockType) && converterType == stockType;
	}

	/// <summary>
	/// Returns the contract of a <c>List&lt;string&gt;</c> or <c>string[]</c> property that the serializer reads with its own
	/// collection converter and its stock converter for string, or null when the property binds as a converter cell.
	/// </summary>
	private static JsonTypeInfo? TryResolveStringListContract(Type propertyType, JsonSerializerOptions options)
	{
		// The decode creates and assigns the list itself, so only the two types it builds qualify. The serializer's list
		// converter reads each element through the options' converter for string, so a converter registered for string, or
		// a factory that hands string another converter, keeps the list on a converter cell.
		if (propertyType != typeof(List<string>) && propertyType != typeof(string[]))
			return null;

		if (JsonMetadataManager.TryResolveTypeInfo(propertyType, options) is not { Kind: JsonTypeInfoKind.Enumerable } typeInfo
			|| typeInfo.ElementType != typeof(string)
			|| !JsonMetadataManager.IsBuiltInConverter(typeInfo.Converter))
			return null;

		if (propertyType == typeof(List<string>) && typeInfo.CreateObject is null)
			return null;

		return UsesBuiltInConverter(typeof(string), options) ? typeInfo : null;
	}
}
