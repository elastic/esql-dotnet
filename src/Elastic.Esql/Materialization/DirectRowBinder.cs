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

/// <summary>Pre-computed binding metadata for a column layout, per column and per branch.</summary>
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
	private static readonly Dictionary<Type, (DirectBinderKind Kind, Type ConverterType)> ScalarKinds = new()
	{
		[typeof(bool)] = (DirectBinderKind.Bool, JsonMetadataServices.BooleanConverter.GetType()),
		[typeof(DateTime)] = (DirectBinderKind.DateTime, JsonMetadataServices.DateTimeConverter.GetType()),
		[typeof(DateTimeOffset)] = (DirectBinderKind.DateTimeOffset, JsonMetadataServices.DateTimeOffsetConverter.GetType()),
		[typeof(decimal)] = (DirectBinderKind.Decimal, JsonMetadataServices.DecimalConverter.GetType()),
		[typeof(double)] = (DirectBinderKind.Double, JsonMetadataServices.DoubleConverter.GetType()),
		[typeof(float)] = (DirectBinderKind.Single, JsonMetadataServices.SingleConverter.GetType()),
		[typeof(Guid)] = (DirectBinderKind.Guid, JsonMetadataServices.GuidConverter.GetType()),
		[typeof(int)] = (DirectBinderKind.Int32, JsonMetadataServices.Int32Converter.GetType()),
		[typeof(long)] = (DirectBinderKind.Int64, JsonMetadataServices.Int64Converter.GetType()),
		[typeof(string)] = (DirectBinderKind.String, JsonMetadataServices.StringConverter.GetType())
	};

	// The serializer keeps its nullable wrapper type internal, and GetNullableConverter creates the wrapper the resolvers
	// create, so a sample's generic definition names it for every underlying type.
	private static readonly Type NullableConverterDefinition = JsonMetadataServices
		.GetNullableConverter(JsonMetadataServices.CreateValueInfo<int>(new JsonSerializerOptions(), JsonMetadataServices.Int32Converter))
		.GetType()
		.GetGenericTypeDefinition();

	// The reflection resolver and a generated context in metadata mode give List<string> and string[] the converter types
	// JsonMetadataServices creates for them. A generated context in the default or serialization mode passes a serialize
	// handler, and the metadata services then wrap that converter in a wrapper type closed over the collection type.
	// Another collection converter of the serializer's assembly can reject a row the inline read accepts, as the IList
	// converter CreateIListInfo builds does for string[], so the assembly alone proves nothing.
	private static readonly Type StringListConverterType = JsonMetadataServices
		.CreateListInfo<List<string>, string>(new JsonSerializerOptions(), new JsonCollectionInfoValues<List<string>>())
		.Converter
		.GetType();
	private static readonly Type StringArrayConverterType = JsonMetadataServices
		.CreateArrayInfo<string>(new JsonSerializerOptions(), new JsonCollectionInfoValues<string[]>())
		.Converter
		.GetType();
	private static readonly Type StringListWrapperType = JsonMetadataServices
		.CreateListInfo<List<string>, string>(new JsonSerializerOptions(), new JsonCollectionInfoValues<List<string>> { SerializeHandler = (_, _) => { } })
		.Converter
		.GetType();
	private static readonly Type StringArrayWrapperType = JsonMetadataServices
		.CreateArrayInfo<string>(new JsonSerializerOptions(), new JsonCollectionInfoValues<string[]> { SerializeHandler = (_, _) => { } })
		.Converter
		.GetType();

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

	/// <summary>
	/// Per column, the depth limit for a cell read on its own reader: the options' limit less the object levels above the
	/// cell in the assembled row, one for the row object and one for each branch.
	/// </summary>
	public required int[] CellMaxDepths { get; init; }

	/// <summary>
	/// Per branch, at its <see cref="ColumnNode.BranchIndex"/>, the parent's property that the branch's object fills. Empty
	/// for a flat layout.
	/// </summary>
	public required JsonPropertyInfo[] BranchProperties { get; init; }

	/// <summary>Per branch, at its <see cref="ColumnNode.BranchIndex"/>, the creator of the nested object. Empty for a flat layout.</summary>
	public required Func<object>[] BranchCreators { get; init; }

	/// <summary>The layout's root, whose children are the result type's columns and branches.</summary>
	public required ColumnNode Root { get; init; }

	/// <summary>Per column, the column's leaf node under <see cref="Root"/>.</summary>
	public required ColumnNode[] Leaves { get; init; }

	/// <summary>
	/// Builds a binder for a flat or nested layout, or returns null when a part of the options' resolver chain can supply
	/// property metadata the binder cannot see, or when the type itself cannot be bound with exact serializer fidelity:
	/// parameterized constructors, serialization callbacks, extension data, number handling on the type or a property, a
	/// property-level converter no cell contract can be built for, populate-style object creation, a required member
	/// without a column, two columns or branches for one property of one object, a mapped property whose JSON name starts
	/// with $ under options that read reference metadata or on a type with polymorphism options, a column whose name equals
	/// the type's discriminator name, a converter cell or a string list cell whose reader would get a depth limit of zero,
	/// or either under options that read reference metadata.
	/// A branch binds through a settable property of its parent without a property-level converter, number handling or
	/// populate-style creation, under the metadata-name rules of a leaf, and needs an index from the layout. Its nested
	/// type meets the rules of the result type, has no polymorphism options, is a reference type and has a column for each
	/// of its required members. A cell's reader gets the options' depth limit less the object levels above the cell.
	/// A <c>List&lt;string&gt;</c> or <c>string[]</c> cell that the serializer reads with its stock list or array
	/// converter, bare or in a generated context's wrapper, and its stock converter for string is read as a string list,
	/// an enum cell on a numeric column that the serializer reads with its own converters, which accept numbers, is read
	/// as an enum, and any other cell that is not one of the built-in scalar kinds deserializes through its own contract.
	/// The nullable form of a built-in scalar kind or of an enum, and a string list in a generated context's wrapper, are
	/// read off the reader only under options with no user-written converter factory and no converter for the property
	/// type.
	/// <paramref name="columns"/> holds the ES|QL type of each column, at the column index of its leaf under
	/// <paramref name="root"/>.
	/// </summary>
	public static DirectRowBinder? TryCreate(
		ColumnNode root,
		ReadOnlySpan<EsqlResponseReader.ColumnInfo> columns,
		JsonTypeInfo typeInfo,
		JsonMetadataManager metadata)
	{
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

		var columnTypes = new string[columns.Length];
		for (var i = 0; i < columns.Length; i++)
			columnTypes[i] = columns[i].Type;

		var builder = new Builder(metadata, columnTypes, Builder.CountBranches(root));
		return builder.TryAddLevel(root, typeInfo, ColumnLayout.EffectiveMaxDepth(metadata.Options) - 1)
			? builder.ToBinder(root, typeInfo.CreateObject)
			: null;
	}

	/// <summary>Walks the column tree one object level at a time and fills the binder's arrays per column and per branch.</summary>
	private sealed class Builder(JsonMetadataManager metadata, string[] columnTypes, int branchCount)
	{
		private readonly JsonMetadataManager _metadata = metadata;
		private readonly JsonSerializerOptions _options = metadata.Options;
		private readonly string[] _columnTypes = columnTypes;
		private readonly bool _readsReferenceMetadata = metadata.Options.ReferenceHandler is { } handler && handler != ReferenceHandler.IgnoreCycles;
		private readonly DirectBinderKind[] _kinds = new DirectBinderKind[columnTypes.Length];
		private readonly JsonPropertyInfo[] _properties = new JsonPropertyInfo[columnTypes.Length];
		private readonly Delegate?[] _typedSetters = new Delegate?[columnTypes.Length];
		private readonly JsonTypeInfo?[] _cellTypeInfos = new JsonTypeInfo?[columnTypes.Length];
		private readonly ConverterCell?[] _converterCells = new ConverterCell?[columnTypes.Length];
		private readonly TypeCode[] _enumTypeCodes = new TypeCode[columnTypes.Length];
		private readonly Type?[] _enumTypes = new Type?[columnTypes.Length];
		private readonly bool[] _isCollection = new bool[columnTypes.Length];
		private readonly int[] _cellMaxDepths = new int[columnTypes.Length];
		private readonly ColumnNode[] _leaves = new ColumnNode[columnTypes.Length];
		private readonly JsonPropertyInfo[] _branchProperties = new JsonPropertyInfo[branchCount];
		private readonly Func<object>[] _branchCreators = new Func<object>[branchCount];
		private int _validatedCellCount;

		public static int CountBranches(ColumnNode node) =>
			(node.Children ?? [])
				.Where(child => child.ColumnIndex < 0)
				.Sum(child => 1 + CountBranches(child));

		/// <summary>
		/// Adds the leaves and branches under <paramref name="node"/>, whose object reads through <paramref name="contract"/>
		/// and whose cells get the depth limit <paramref name="cellMaxDepth"/>, or returns false at the first rule one misses.
		/// </summary>
		public bool TryAddLevel(ColumnNode node, JsonTypeInfo contract, int cellMaxDepth)
		{
			// Two branches of one nested type share its contract's property infos, so a property counts as bound twice only
			// within one object.
			var bound = new HashSet<JsonPropertyInfo>();

			foreach (var child in node.Children ?? [])
			{
				var added = child.ColumnIndex >= 0
					? TryAddLeaf(child, contract, cellMaxDepth, bound)
					: TryAddBranch(child, contract, cellMaxDepth, bound);
				if (!added)
					return false;
			}

			// A required member without a column fails every row that holds its object in the serializer, so keep that
			// behavior on the slow path.
			foreach (var property in contract.Properties)
			{
				if (property.IsRequired && !bound.Contains(property))
					return false;
			}

			return true;
		}

		public DirectRowBinder ToBinder(ColumnNode root, Func<object> createObject) =>
			new()
			{
				Kinds = _kinds,
				Properties = _properties,
				TypedSetters = _typedSetters,
				CellTypeInfos = _cellTypeInfos,
				ConverterCells = _converterCells,
				EnumTypeCodes = _enumTypeCodes,
				EnumTypes = _enumTypes,
				IsCollection = _isCollection,
				CreateObject = createObject,
				ValidatedCellCount = _validatedCellCount,
				CellMaxDepths = _cellMaxDepths,
				BranchProperties = _branchProperties,
				BranchCreators = _branchCreators,
				Root = root,
				Leaves = _leaves
			};

		private bool TryAddLeaf(ColumnNode leaf, JsonTypeInfo contract, int cellMaxDepth, HashSet<JsonPropertyInfo> bound)
		{
			var property = FindProperty(contract, leaf.PropertyName, _options.PropertyNameCaseInsensitive);
			if (property is null || property.Set is null || property.IsExtensionData || property.NumberHandling is not null)
				return false;

			if (ReadsAsMetadata(leaf, property, contract))
				return false;

			// Two columns for one property, or populate-style creation, are the serializer's business.
			if (!bound.Add(property) || EffectiveCreationHandling(property, contract, _options) == JsonObjectCreationHandling.Populate)
				return false;

			var column = leaf.ColumnIndex;
			_properties[column] = property;
			_isCollection[column] = leaf.IsCollection;
			_leaves[column] = leaf;
			_cellMaxDepths[column] = cellMaxDepth;
			return TryClassifyCell(column, property, cellMaxDepth);
		}

		private bool TryClassifyCell(int column, JsonPropertyInfo property, int cellMaxDepth)
		{
			// Checked before the built-in kinds, which would read the cell without the property's converter.
			if (property.CustomConverter is null && TryClassifyScalar(property.PropertyType, _options, out var kind))
			{
				_kinds[column] = kind;
				// The modifier-free chain carries no replaced Set, so a setter compiled for the member assigns what Set would.
				_typedSetters[column] = _metadata.GetOrCompileSetter(property, kind);
				return true;
			}

			// An enum cell needs no cell reader and reads no reference metadata, so the converter cell conditions do not apply.
			if (TryClassifyEnum(property, _columnTypes[column], _options, out var enumType))
			{
				_kinds[column] = DirectBinderKind.Enum;
				_enumTypeCodes[column] = Type.GetTypeCode(enumType);
				_enumTypes[column] = enumType;
				_typedSetters[column] = _metadata.GetOrCompileSetter(property, DirectBinderKind.Enum);
				_validatedCellCount++;
				return true;
			}

			// Converter cells and string list cells bind only under these conditions. Each object of the assembled row that
			// holds a cell sits above it, so a cell reader gets the options' limit less those levels. Where that leaves zero,
			// which a reader takes for the default, it could not reject a nested value the way the serializer rejects the
			// assembled row, and the serializer rejects a string list cell's array there too. The assemble path serves these
			// rows instead.
			// Each converter cell deserializes in a call of its own, so an $id or $ref that spans cells resolves differently
			// than in the serializer's single call over the row. IgnoreCycles reads no metadata and keeps the direct path.
			if (cellMaxDepth < 1 || _readsReferenceMetadata)
				return false;

			if (property.CustomConverter is null && TryResolveStringListContract(property.PropertyType, _options) is { } listTypeInfo)
			{
				_kinds[column] = property.PropertyType == typeof(string[]) ? DirectBinderKind.StringArray : DirectBinderKind.StringList;
				if (_kinds[column] == DirectBinderKind.StringList)
					_cellTypeInfos[column] = listTypeInfo;

				_typedSetters[column] = _metadata.GetOrCompileSetter(property, _kinds[column]);
				_validatedCellCount++;
				return true;
			}

			var cellTypeInfo = property.CustomConverter is not null
				? _metadata.GetOrCreatePropertyContract(property)
				: JsonMetadataManager.TryResolveTypeInfo(property.PropertyType, _options);
			if (cellTypeInfo is null)
				return false;

			_kinds[column] = DirectBinderKind.Converter;
			_converterCells[column] = _metadata.GetOrCreateConverterCell(property, cellTypeInfo);
			_validatedCellCount++;
			return true;
		}

		private bool TryAddBranch(ColumnNode branch, JsonTypeInfo contract, int cellMaxDepth, HashSet<JsonPropertyInfo> bound)
		{
			// The layout marks the root by its empty name, so it indexes no branch reached through a property whose JSON name
			// is empty, and such a layout keeps the batched path.
			if (branch.BranchIndex < 0)
				return false;

			if (FindBranchProperty(branch, contract) is not { } property || !bound.Add(property))
				return false;

			var nested = JsonMetadataManager.TryResolveTypeInfo(property.PropertyType, _options);
			if (nested is not { Kind: JsonTypeInfoKind.Object, CreateObject: { } createObject }
				|| nested.OnDeserializing is not null
				|| nested.OnDeserialized is not null
				|| nested.NumberHandling is not null
				|| nested.PolymorphismOptions is not null
				|| property.PropertyType.IsValueType)
				return false;

			// A branch assigns its nested object through Set, which boxes nothing for the reference type a nested type must
			// be, so no setter is compiled for it.
			_branchProperties[branch.BranchIndex] = property;
			_branchCreators[branch.BranchIndex] = createObject;
			return TryAddLevel(branch, nested, cellMaxDepth - 1);
		}

		private JsonPropertyInfo? FindBranchProperty(ColumnNode branch, JsonTypeInfo contract)
		{
			var property = FindProperty(contract, branch.PropertyName, caseInsensitive: false);
			if (property is null
				|| property.Set is null
				|| property.CustomConverter is not null
				|| property.IsExtensionData
				|| property.NumberHandling is not null)
				return null;

			return ReadsAsMetadata(branch, property, contract) || EffectiveCreationHandling(property, contract, _options) == JsonObjectCreationHandling.Populate
				? null
				: property;
		}

		private bool ReadsAsMetadata(ColumnNode node, JsonPropertyInfo property, JsonTypeInfo contract)
		{
			// The serializer takes a property name with a leading $ for metadata under a handler that reads reference metadata
			// and on a type whose derived types carry a type discriminator. Under such a handler it reads $id and $ref as
			// reference metadata, which must hold a string, and rejects any other such name, and on such a type it rejects
			// every such name, so a row with such a column never binds through it, whatever the cell's kind. The check takes
			// every type with polymorphism options, so a type whose derived types carry no discriminator loses the direct
			// path for such a column and keeps its values.
			if ((_readsReferenceMetadata || contract.PolymorphismOptions is not null) && property.Name.Length > 0 && property.Name[0] == '$')
				return true;

			// A custom discriminator name is metadata without a $. The serializer reads such a column as the type discriminator,
			// in first position under the default options and in any position under AllowOutOfOrderMetadataProperties, and never
			// binds it to the property. It compares the name ordinally with the name in the row object, the column's own, which
			// a leaf's lookup can map to a property whose name differs in case.
			return string.Equals(node.PropertyName, contract.PolymorphismOptions?.TypeDiscriminatorPropertyName, StringComparison.Ordinal);
		}
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
		var found = ScalarKinds.TryGetValue(Nullable.GetUnderlyingType(propertyType) ?? propertyType, out var entry);
		kind = entry.Kind;
		return found;
	}

	/// <summary>
	/// Whether the options read <paramref name="propertyType"/> with the serializer's stock converter for it, and a nullable
	/// value type with the serializer's own nullable wrapper around the stock converter of the type under it.
	/// </summary>
	private static bool UsesBuiltInConverter(Type propertyType, JsonSerializerOptions options)
	{
		if (JsonMetadataManager.TryResolveTypeInfo(propertyType, options) is not { } propertyTypeInfo
			|| !IsStockConverter(propertyType, propertyTypeInfo.Converter))
			return false;

		var underlying = Nullable.GetUnderlyingType(propertyType);
		if (underlying is null)
			return true;

		// A converter or factory the serializer chose for the nullable type when it resolved the contract replaced the
		// default wrapper with its own product, which can be the serializer's wrapper around a custom converter, so the
		// wrapper's type tells nothing about what it holds.
		if (OptionsMayReplaceConverter(propertyType, options))
			return false;

		// The default wrapper reads through the converter for the type under it, so a user converter for T disqualifies
		// T? as well.
		return JsonMetadataManager.TryResolveTypeInfo(underlying, options) is { } underlyingTypeInfo
			&& IsStockConverter(underlying, underlyingTypeInfo.Converter);
	}

	// A converter names its target through Type, which JsonConverter<T> seals, so reading it runs no user code. A user
	// factory answers through CanConvert, whose answer now need not be the one the serializer got, so its presence alone
	// counts, while the serializer's own factories keep a pure predicate. Under the modifier-free chain the options'
	// converters are the one place a replacement for the resolver's converter comes from.
	private static bool OptionsMayReplaceConverter(Type type, JsonSerializerOptions options) =>
		options.Converters.Any(
			converter => converter.Type == type || (converter is JsonConverterFactory && !JsonMetadataManager.IsBuiltInConverter(converter))
		);

	private static bool IsStockConverter(Type type, JsonConverter converter)
	{
		// The stock enum converter is created per enum type, so any converter of the serializer's assembly qualifies and the
		// probe of 0 judges what it accepts.
		if (type.IsEnum)
			return JsonMetadataManager.IsBuiltInConverter(converter);

		var converterType = converter.GetType();
		if (Nullable.GetUnderlyingType(type) is not null)
			return converterType.IsGenericType && converterType.GetGenericTypeDefinition() == NullableConverterDefinition;

		return ScalarKinds.TryGetValue(type, out var entry) && converterType == entry.ConverterType;
	}

	/// <summary>
	/// Returns the contract of a <c>List&lt;string&gt;</c> or <c>string[]</c> property that the serializer reads with its
	/// stock list or array converter, bare or in a generated context's wrapper, and its stock converter for string, or
	/// null when the property binds as a converter cell.
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
			|| !IsStockStringListConverter(propertyType, typeInfo.Converter, options))
			return null;

		if (propertyType == typeof(List<string>) && typeInfo.CreateObject is null)
			return null;

		return UsesBuiltInConverter(typeof(string), options) ? typeInfo : null;
	}

	private static bool IsStockStringListConverter(Type propertyType, JsonConverter converter, JsonSerializerOptions options)
	{
		var isArray = propertyType == typeof(string[]);
		var converterType = converter.GetType();
		if (converterType == (isArray ? StringArrayConverterType : StringListConverterType))
			return true;

		// The wrapper hides the converter it wraps, and the metadata services give any converter they build for the
		// collection type with a serialize handler the same wrapper type, the IList converter CreateIListInfo builds for
		// string[] among them. Under the modifier-free chain, on options with no converter for the type and no user-written
		// factory, a wrapper comes only from a generated context's own contract for the declared shape: the generator calls
		// CreateListInfo for a List<T> and CreateArrayInfo for an array, and a converter registered on the options replaces
		// that contract with a value contract around the registered instance.
		return converterType == (isArray ? StringArrayWrapperType : StringListWrapperType) && !OptionsMayReplaceConverter(propertyType, options);
	}
}
