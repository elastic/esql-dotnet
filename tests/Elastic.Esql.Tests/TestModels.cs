// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Esql.Tests.TypeMapping.Escaping;

namespace Elastic.Esql.Tests;

// ============================================================================
// MAPPING CONTEXT: registers all test types for ES|QL tests
// ============================================================================

[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(TaggedProduct))]
[JsonSerializable(typeof(SetTaggedProduct))]
[JsonSerializable(typeof(FrozenTaggedProduct))]
[JsonSerializable(typeof(ImmutableTaggedProduct))]
[JsonSerializable(typeof(CollectionTaggedProduct))]
[JsonSerializable(typeof(InterfaceTaggedProduct))]
[JsonSerializable(typeof(TypedValuesProduct))]
[JsonSerializable(typeof(LinkedProduct))]
[JsonSerializable(typeof(LabeledProduct))]
[JsonSerializable(typeof(ArchivedLinesProduct))]
[JsonSerializable(typeof(TreeNode))]
[JsonSerializable(typeof(OptionalDocument))]
[JsonSerializable(typeof(OptionalCountProjection))]
[JsonSerializable(typeof(EagerNestedDocument))]
[JsonSerializable(typeof(EagerHostRecord))]
[JsonSerializable(typeof(LazyHostRecord))]
[JsonSerializable(typeof(NestedSelectionHostWithTag))]
[JsonSerializable(typeof(PrefixedCodeDocument))]
[JsonSerializable(typeof(ConvertedTagsProduct))]
[JsonSerializable(typeof(TypeConvertedTagsProduct))]
[JsonSerializable(typeof(LinedProduct))]
[JsonSerializable(typeof(AttributedProduct))]
[JsonSerializable(typeof(NullableNestedModel))]
[JsonSerializable(typeof(AddressModel))]
[JsonSerializable(typeof(SimpleDocument))]
[JsonSerializable(typeof(MetricDocument))]
[JsonSerializable(typeof(EventDocument))]
[JsonSerializable(typeof(LanguageLookup))]
[JsonSerializable(typeof(ThreatListEntry))]
[JsonSerializable(typeof(OverlappingLookup))]
[JsonSerializable(typeof(LogProjection))]
[JsonSerializable(typeof(StatsProjection))]
[JsonSerializable(typeof(OrdinalEnumDocument))]
[JsonSerializable(typeof(CustomConverterDocument))]
[JsonSerializable(typeof(ConvertedDurationDocument))]
[JsonSerializable(typeof(RecordProjection))]
[JsonSerializable(typeof(UnmatchedCtorProjection))]
[JsonSerializable(typeof(CollisionRecord))]
[JsonSerializable(typeof(NestedSelectionDocument))]
[JsonSerializable(typeof(NestedHostLookup))]
[JsonSerializable(typeof(DottedLevelLookup))]
[JsonSerializable(typeof(BookDocument))]
[JsonSerializable(typeof(BookProjection))]
[JsonSerializable(typeof(DottedJsonNameDocument))]
[JsonSerializable(typeof(SpecialCharacterDocument))]
[JsonSerializable(typeof(SpecialCharacterLookup))]
[JsonSerializable(typeof(SpecialCharacterProjection))]
[JsonSerializable(typeof(ColumnNameEscapingTests.EqualsSignTarget))]
public sealed partial class EsqlTestMappingContext : JsonSerializerContext;

/// <summary>A recursive document: a node whose child is a node, so a projection to the child keeps the type.</summary>
public class TreeNode
{
	public string Name { get; set; } = string.Empty;

	public TreeNode? Child { get; set; }
}

/// <summary>
/// Document whose members are all nullable: the compiler then records the annotation
/// once on the type, as a NullableContext, rather than on each member.
/// </summary>
public class OptionalDocument
{
	public string? Message { get; set; }

	public string? ClientIp { get; set; }
}

/// <summary>Serializes a code as its prefixed form, so a compared value has to go through it.</summary>
public class PrefixedCodeConverter : JsonConverter<string>
{
	public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		(reader.GetString() ?? throw new JsonException("Expected a string.")).Replace("CODE-", "");

	public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
		writer.WriteStringValue($"CODE-{value}");
}

/// <summary>Document whose string property is serialized through a converter of its own.</summary>
public class PrefixedCodeDocument
{
	[JsonConverter(typeof(PrefixedCodeConverter))]
	public string Code { get; set; } = string.Empty;
}

/// <summary>A document whose nested member is declared non-nullable, with an initializer.</summary>
public class EagerNestedDocument
{
	public string Message { get; set; } = string.Empty;

	public NestedSelectionHost Host { get; set; } = new();
}

/// <summary>A record whose constructor takes the nested child as non-nullable: it cannot hold a guard's null.</summary>
public record EagerHostRecord(NestedSelectionHost Host);

/// <summary>The same record with the child declared nullable, where a guard's null has a place to go.</summary>
public record LazyHostRecord(NestedSelectionHost? Host);

/// <summary>A nested child whose constructor takes a value, for the guard over a constructor argument.</summary>
public class NestedSelectionHostWithTag(string tag)
{
	public string Tag { get; } = tag;

	public string Name { get; set; } = string.Empty;
}

/// <summary>Projection whose only member is a nullable value type, which holds a guard's null.</summary>
public class OptionalCountProjection
{
	public int? Count { get; set; }
}

/// <summary>
/// Document with multi-value fields, for predicates over collections.
/// </summary>
public class TaggedProduct
{
	public string Name { get; set; } = string.Empty;

	public string[] Tags { get; set; } = [];

	public List<string> Categories { get; set; } = [];

	public List<int> Ratings { get; set; } = [];
}

/// <summary>Document whose tags are a set: a set answers Contains by its own comparer.</summary>
public class SetTaggedProduct
{
	public HashSet<string> Tags { get; set; } = [];
}

/// <summary>Document whose tags are a frozen set: the base library keeps it in a namespace of its own.</summary>
public class FrozenTaggedProduct
{
#pragma warning disable IDE0301 // [] builds a FrozenSet only from .NET 9 on, and the tests run on .NET 8 as well
	public FrozenSet<string> Tags { get; set; } = FrozenSet<string>.Empty;
#pragma warning restore IDE0301
}

/// <summary>Document whose tags are an immutable array, from the base library's immutable collections.</summary>
public class ImmutableTaggedProduct
{
	public ImmutableArray<string> Tags { get; set; } = [];
}

/// <summary>Document whose tags are a Collection, from the base library's object model.</summary>
public class CollectionTaggedProduct
{
	public Collection<string> Tags { get; set; } = [];
}

/// <summary>Document whose tags are typed as an interface, which says nothing about the collection.</summary>
public class InterfaceTaggedProduct
{
	public ICollection<string> Tags { get; set; } = [];
}

/// <summary>
/// Document with multi-value fields whose values C# compares through a conversion, as it does
/// enums and the narrow integers, or with a value computed when the query runs, as a date is.
/// </summary>
public class TypedValuesProduct
{
	public List<Priority> Priorities { get; set; } = [];

	public List<Grade> Grades { get; set; } = [];

	public List<short> Sizes { get; set; } = [];

	public List<byte> Scores { get; set; } = [];

	public List<DateTime> Restocks { get; set; } = [];
}

/// <summary>Document whose links are a class the serializer writes as a string.</summary>
public class LinkedProduct
{
	public List<Uri> Links { get; set; } = [];
}

/// <summary>Document whose labels are dictionaries, each of them one object in the mapping.</summary>
public class LabeledProduct
{
	public List<Dictionary<string, string>> Labels { get; set; } = [];
}

/// <summary>
/// Document whose lines the serializer never writes, so that it has no contract for their type:
/// nothing else in the mapping context refers to <see cref="ArchivedLine"/>.
/// </summary>
public class ArchivedLinesProduct
{
	public string Name { get; set; } = string.Empty;

	[JsonIgnore]
	public List<ArchivedLine> Lines { get; set; } = [];
}

/// <summary>A line held only by an ignored property.</summary>
public class ArchivedLine
{
	public string Sku { get; set; } = string.Empty;
}

/// <summary>Enum written by name wherever it appears, through the converter on the type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Grade>))]
public enum Grade
{
	Low,
	High
}

/// <summary>Writes each tag in its prefixed form, so the field holds values the query was not given.</summary>
public class PrefixedTagsConverter : JsonConverter<List<string>>
{
	public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var tags = new List<string>();

		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
			tags.Add((reader.GetString() ?? string.Empty).Replace("TAG-", ""));

		return tags;
	}

	public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		foreach (var tag in value)
			writer.WriteStringValue($"TAG-{tag}");

		writer.WriteEndArray();
	}
}

/// <summary>A collection type that names its own converter, which then writes every field of the type.</summary>
[JsonConverter(typeof(PrefixedTagListConverter))]
public class PrefixedTagList : List<string>;

/// <summary>The same prefixing, for the collection type that carries it.</summary>
public class PrefixedTagListConverter : JsonConverter<PrefixedTagList>
{
	public override PrefixedTagList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var tags = new PrefixedTagList();

		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
			tags.Add((reader.GetString() ?? string.Empty).Replace("TAG-", ""));

		return tags;
	}

	public override void Write(Utf8JsonWriter writer, PrefixedTagList value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		foreach (var tag in value)
			writer.WriteStringValue($"TAG-{tag}");

		writer.WriteEndArray();
	}
}

/// <summary>Document whose tags are a collection type with a converter of its own.</summary>
public class TypeConvertedTagsProduct
{
	public PrefixedTagList Tags { get; set; } = [];
}

/// <summary>Document whose tags are serialized through a converter of their own.</summary>
public class ConvertedTagsProduct
{
	[JsonConverter(typeof(PrefixedTagsConverter))]
	public List<string> Tags { get; set; } = [];
}

/// <summary>An element of a collection of objects.</summary>
public class ProductLine
{
	public string Sku { get; set; } = string.Empty;
}

/// <summary>Document holding a collection of objects, which the mapping stores as an object.</summary>
public class LinedProduct
{
	public List<ProductLine> Lines { get; set; } = [];
}

/// <summary>Document holding a dictionary, which the mapping stores as one object.</summary>
public class AttributedProduct
{
	public Dictionary<string, int> Attributes { get; set; } = [];
}

/// <summary>Test document with dense_vector fields for KNN / V_* tests.</summary>
public class BookDocument
{
	public string Title { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public DenseVector<float> TitleVec { get; set; }
	public DenseVector<byte> RgbVector { get; set; }
}

/// <summary>Result projection for FORK/FUSE tests that includes metadata-derived columns.</summary>
public class BookProjection
{
	public string Id { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public float Score { get; set; }
}

/// <summary>
/// Primary test document type with various field types and attributes.
/// </summary>
public class LogEntry
{
	[JsonPropertyName("@timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("log.level")]
	public string Level { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;  // → "message"

	public int StatusCode { get; set; }  // → "statusCode"

	public double Duration { get; set; }  // → "duration"

	public bool IsError { get; set; }  // → "isError"

	public string? ClientIp { get; set; }  // → "clientIp"

	public string? ServerName { get; set; }  // → "serverName"

	[JsonIgnore]
	public string InternalId { get; set; } = string.Empty;
}

/// <summary>
/// Simple document type without attributes for default naming tests.
/// </summary>
public class SimpleDocument
{
	public string Name { get; set; } = string.Empty;
	public int Value { get; set; }
	public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Document with nullable properties.
/// </summary>
public class MetricDocument
{
	public DateTime Timestamp { get; set; }
	public string Name { get; set; } = string.Empty;
	public double? Value { get; set; }
	public int? Count { get; set; }
	public string? Tags { get; set; }
}

/// <summary>
/// Lookup document for LOOKUP JOIN tests.
/// </summary>
public class LanguageLookup
{
	public int LanguageCode { get; set; }
	public string LanguageName { get; set; } = string.Empty;
}

/// <summary>
/// Lookup document for IP threat correlation LOOKUP JOIN tests.
/// </summary>
public class ThreatListEntry
{
	public string ClientIp { get; set; } = string.Empty;
	public string ThreatLevel { get; set; } = string.Empty;
}

/// <summary>
/// Lookup document that shares field names with <see cref="LogEntry"/> for join collision tests.
/// Fields <c>Message</c> and <c>ClientIp</c> collide with the outer type.
/// </summary>
public class OverlappingLookup
{
	public string ClientIp { get; set; } = string.Empty;
	public string Message { get; set; } = string.Empty;
	public string Region { get; set; } = string.Empty;
}

public class NestedHostLookup
{
	public string Message { get; set; } = string.Empty;
	public NestedSelectionHost Host { get; set; } = new();
}

public class DottedLevelLookup
{
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("log.level")]
	public string Level { get; set; } = string.Empty;
}

/// <summary>
/// Enum for testing enum formatting.
/// </summary>
public enum LogLevel
{
	Debug,
	Info,
	Warning,
	Error,
	Critical
}

/// <summary>
/// Document with enum property using string serialization.
/// </summary>
public class EventDocument
{
	public DateTime Timestamp { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
	public LogLevel Level { get; set; }

	public string Message { get; set; } = string.Empty;
	public Guid EventId { get; set; }
}

/// <summary>
/// Strongly-typed projection model with custom JSON field names for testing
/// that <see cref="JsonPropertyNameAttribute"/> is honored on target types.
/// </summary>
public class LogProjection
{
	[JsonPropertyName("log_level")]
	public string Level { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("status")]
	public int StatusCode { get; set; }

	public double Duration { get; set; }
}

/// <summary>
/// Strongly-typed stats result model for testing GroupBy projections
/// with <see cref="JsonPropertyNameAttribute"/> on result fields.
/// </summary>
public class StatsProjection
{
	[JsonPropertyName("log_level")]
	public string Level { get; set; } = string.Empty;

	[JsonPropertyName("total_count")]
	public int Count { get; set; }

	[JsonPropertyName("avg_duration")]
	public double AvgDuration { get; set; }

	[JsonPropertyName("total_duration")]
	public double TotalDuration { get; set; }
}

/// <summary>
/// Enum for testing ordinal (integer) enum serialization.
/// </summary>
public enum Priority
{
	Low,
	Medium,
	High,
	Critical
}

/// <summary>
/// Document with an enum property that uses default ordinal serialization (no <see cref="JsonStringEnumConverter"/>).
/// </summary>
public class OrdinalEnumDocument
{
	public Priority Priority { get; set; }
	public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Custom converter that serializes an <see cref="int"/> as a prefixed string.
/// </summary>
public class PrefixedIntConverter : JsonConverter<int>
{
	public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var s = reader.GetString() ?? throw new JsonException("Expected a string.");
		return int.Parse(s.Replace("ID-", ""), System.Globalization.CultureInfo.InvariantCulture);
	}

	public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
		writer.WriteStringValue($"ID-{value}");
}

/// <summary>
/// Document with a custom converter on a property, validating that user-provided converters are respected.
/// </summary>
public class CustomConverterDocument
{
	[JsonConverter(typeof(PrefixedIntConverter))]
	public int CustomId { get; set; }

	public string Name { get; set; } = string.Empty;
}

/// <summary>Serializes a <see cref="TimeSpan"/> as whole milliseconds, as a duration stored in a numeric column would be.</summary>
public class MillisecondsTimeSpanConverter : JsonConverter<TimeSpan>
{
	public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		TimeSpan.FromMilliseconds(reader.GetInt64());

	public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
		writer.WriteNumberValue((long)value.TotalMilliseconds);
}

/// <summary>Document whose duration carries a property-level converter that must win over the default duration literal.</summary>
public class ConvertedDurationDocument
{
	[JsonConverter(typeof(MillisecondsTimeSpanConverter))]
	public TimeSpan Duration { get; set; }

	public string Name { get; set; } = string.Empty;
}

/// <summary>Record projection for testing constructor-call Select.</summary>
public record RecordProjection(string Message, int StatusCode);

/// <summary>
/// Projection whose constructor parameter name does not match any property,
/// used to verify the translator throws. Cannot use a primary constructor here
/// because that would create a matching property for the parameter.
/// </summary>
#pragma warning disable IDE0290
public class UnmatchedCtorProjection
{
	public UnmatchedCtorProjection(string noSuchField) => Name = noSuchField;

	public string Name { get; }
}
#pragma warning restore IDE0290

/// <summary>Record projection for join collision tests with constructor-call syntax.</summary>
public record CollisionRecord(string OuterMsg, string InnerMsg);

/// <summary>Translation test model for nested sub-field selection and wildcard KEEP behavior.</summary>
public class NestedSelectionDocument
{
	public string Message { get; set; } = string.Empty;

	public NestedSelectionHost? Host { get; set; }

	public NestedSelectionAgent? Agent { get; set; }
}

public class NestedSelectionHost
{
	public string Name { get; set; } = string.Empty;

	public NestedSelectionGeo? Geo { get; set; }
}

public class NestedSelectionAgent
{
	public string Name { get; set; } = string.Empty;
}

public class NestedSelectionGeo
{
	public string City { get; set; } = string.Empty;
}

/// <summary>Document with a JSON property name containing a dot, which the translator treats as a nested path.</summary>
public class DottedJsonNameDocument
{
	[JsonPropertyName("a.b")]
	public string? Value { get; set; }
}

/// <summary>Document whose JSON field names require backtick quoting in ES|QL.</summary>
public class SpecialCharacterDocument
{
	[JsonPropertyName("user-agent")]
	public UserAgentInfo UserAgent { get; set; } = new();

	[JsonPropertyName("response size")]
	public int ResponseSize { get; set; }

	public string Message { get; set; } = string.Empty;
}

public class UserAgentInfo
{
	[JsonPropertyName("os name")]
	public string OsName { get; set; } = string.Empty;

	public string Version { get; set; } = string.Empty;
}

/// <summary>Lookup document sharing the quoted "user-agent" field name for join collision tests.</summary>
public class SpecialCharacterLookup
{
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("user-agent")]
	public string UserAgent { get; set; } = string.Empty;
}

/// <summary>Projection record whose JSON name requires quoting, for constructor-call Select tests.</summary>
public record SpecialCharacterProjection([property: JsonPropertyName("user-agent")] string UserAgent);

// ============================================================================
// MATERIALIZATION TEST MODELS: used by deserialization edge-case tests
// ============================================================================

[JsonSerializable(typeof(ArrayStringPropertyModel))]
[JsonSerializable(typeof(ListIntPropertyModel))]
[JsonSerializable(typeof(ListStringPropertyModel))]
[JsonSerializable(typeof(ScalarStringModel))]
[JsonSerializable(typeof(ScalarIntModel))]
[JsonSerializable(typeof(ScalarDoubleModel))]
[JsonSerializable(typeof(NullableIntModel))]
[JsonSerializable(typeof(AllNullableModel))]
[JsonSerializable(typeof(NonNullableValueModel))]
[JsonSerializable(typeof(BoolOnlyModel))]
[JsonSerializable(typeof(GuidPropertyModel))]
[JsonSerializable(typeof(DateTimeOffsetPropertyModel))]
[JsonSerializable(typeof(LongPropertyModel))]
[JsonSerializable(typeof(CustomConverterDocument))]
[JsonSerializable(typeof(PersonModel))]
[JsonSerializable(typeof(DeepRoot))]
[JsonSerializable(typeof(DotNamePrecedenceModel))]
[JsonSerializable(typeof(MixedDotModel))]
[JsonSerializable(typeof(OuterWithDotInner))]
[JsonSerializable(typeof(PersonWithTaggedAddress))]
[JsonSerializable(typeof(MultiNestedModel))]
[JsonSerializable(typeof(NullableNestedModel))]
[JsonSerializable(typeof(FlatDotFallbackModel))]
[JsonSerializable(typeof(Level1Root))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
public sealed partial class MaterializationTestJsonContext : JsonSerializerContext;

public class ArrayStringPropertyModel
{
	public string[] Tags { get; set; } = [];
	public string Name { get; set; } = string.Empty;
}

public class ListIntPropertyModel
{
	public List<int> Values { get; set; } = [];
	public string Name { get; set; } = string.Empty;
}

public class ListStringPropertyModel
{
	public List<string> Items { get; set; } = [];
}

public class ScalarStringModel
{
	public string Value { get; set; } = string.Empty;
	public int Count { get; set; }
}

public class ScalarIntModel
{
	public int Value { get; set; }
	public string Name { get; set; } = string.Empty;
}

public class ScalarDoubleModel
{
	public double Value { get; set; }
}

public class NullableIntModel
{
	public int? Value { get; set; }
	public string Name { get; set; } = string.Empty;
}

public class AllNullableModel
{
	public string? Name { get; set; }
	public int? Count { get; set; }
	public double? Score { get; set; }
}

public class NonNullableValueModel
{
	public int Count { get; set; }
	public bool Active { get; set; }
	public double Score { get; set; }
}

public class GuidPropertyModel
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
}

public class DateTimeOffsetPropertyModel
{
	public DateTimeOffset Timestamp { get; set; }
	public string Name { get; set; } = string.Empty;
}

public class LongPropertyModel
{
	public long Value { get; set; }
	public string Name { get; set; } = string.Empty;
}

public class BoolOnlyModel
{
	public bool Active { get; set; }
}

// ============================================================================
// NESTED OBJECT TEST MODELS: used by nested object deserialization tests
// ============================================================================

public class AddressModel
{
	public string Street { get; set; } = string.Empty;
	public string City { get; set; } = string.Empty;
}

public class PersonModel
{
	public string Name { get; set; } = string.Empty;
	public AddressModel? Address { get; set; }
}

public class DeepLeaf
{
	public string Value { get; set; } = string.Empty;
}

public class DeepMiddle
{
	public string Label { get; set; } = string.Empty;
	public DeepLeaf? Leaf { get; set; }
}

public class DeepRoot
{
	public string Name { get; set; } = string.Empty;
	public DeepMiddle? Middle { get; set; }
}

public class Level4Leaf
{
	public string Data { get; set; } = string.Empty;
}

public class Level3
{
	public Level4Leaf? Inner { get; set; }
	public string Tag { get; set; } = string.Empty;
}

public class Level2
{
	public Level3? Child { get; set; }
	public string Info { get; set; } = string.Empty;
}

public class Level1Root
{
	public string Name { get; set; } = string.Empty;
	public Level2? Nested { get; set; }
}

/// <summary>
/// JsonPropertyName with dots takes precedence over nested object resolution.
/// Column "address.street" should map to this flat property, NOT create a nested object.
/// </summary>
public class DotNamePrecedenceModel
{
	[JsonPropertyName("address.street")]
	public string AddressStreet { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Mixed scenario: "address.full" is a flat JsonPropertyName, while "address.street"/"address.city"
/// resolve to the nested Address object.
/// </summary>
public class MixedDotModel
{
	[JsonPropertyName("address.full")]
	public string AddressFull { get; set; } = string.Empty;

	public AddressModel? Address { get; set; }
}

public class InnerWithDotName
{
	[JsonPropertyName("x.y")]
	public string Xy { get; set; } = string.Empty;

	public string Z { get; set; } = string.Empty;
}

public class OuterWithDotInner
{
	public InnerWithDotName? Inner { get; set; }
}

public class AddressWithTags
{
	public string City { get; set; } = string.Empty;
	public List<string> Tags { get; set; } = [];
}

public class PersonWithTaggedAddress
{
	public string Name { get; set; } = string.Empty;
	public AddressWithTags? Address { get; set; }
}

public class ContactInfo
{
	public string Email { get; set; } = string.Empty;
	public string Phone { get; set; } = string.Empty;
}

public class MultiNestedModel
{
	public string Name { get; set; } = string.Empty;
	public AddressModel? Address { get; set; }
	public ContactInfo? Contact { get; set; }
}

public class NullableNestedModel
{
	public string Name { get; set; } = string.Empty;
	public AddressModel? Address { get; set; }
}

/// <summary>
/// Column with dots but no matching nested type - should fall back to flat property name.
/// </summary>
public class FlatDotFallbackModel
{
	[JsonPropertyName("unknown.prop")]
	public string UnknownProp { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;
}
