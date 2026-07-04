// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
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
	Guid
}

/// <summary>
/// Pre-computed per-column binding metadata for flat column layouts. Enables reading each cell
/// directly off the <see cref="Utf8JsonReader"/> and assigning it via <see cref="JsonPropertyInfo.Set"/>,
/// bypassing the assemble-and-reparse pipeline. Built once per (target type, column schema) and
/// cached on the <see cref="ColumnLayout"/>; immutable after construction because layouts are
/// shared across threads.
/// </summary>
/// <remarks>
/// <see cref="JsonPropertyInfo.Set"/> is object-typed, so value-type cells box once per cell. For the
/// primary flat-DTO case this is still a large net win over tokenizing, re-writing, and re-parsing
/// every cell. No Expression.Compile or MakeGenericType is involved, keeping the path AOT-safe.
/// </remarks>
internal sealed class DirectRowBinder
{
	public required DirectBinderKind[] Kinds { get; init; }
	public required JsonPropertyInfo[] Properties { get; init; }
	public required Func<object> CreateObject { get; init; }
	public Action<object>? OnDeserializing { get; init; }
	public Action<object>? OnDeserialized { get; init; }

	/// <summary>
	/// Builds a binder for a flat layout, or returns null when any column cannot be bound with
	/// exact serializer fidelity (custom converters, required members, unmapped or ambiguous
	/// columns, non-scalar property types, parameterized constructors).
	/// </summary>
	public static DirectRowBinder? TryCreate(ColumnNode[] leafNodes, JsonTypeInfo typeInfo, JsonSerializerOptions options)
	{
		if (typeInfo.Kind != JsonTypeInfoKind.Object || typeInfo.CreateObject is null)
			return null;

		// A null cell makes the fast path skip the assignment while the serializer would fail the
		// row for a missing required member - keep required-member types on the slow path.
		foreach (var property in typeInfo.Properties)
		{
			if (property.IsRequired)
				return null;
		}

		var kinds = new DirectBinderKind[leafNodes.Length];
		var properties = new JsonPropertyInfo[leafNodes.Length];

		for (var i = 0; i < leafNodes.Length; i++)
		{
			var property = FindProperty(typeInfo, leafNodes[i].PropertyName, options.PropertyNameCaseInsensitive);
			if (property is null || property.Set is null || property.CustomConverter is not null)
				return null;

			if (!TryClassify(property.PropertyType, out var kind) || !UsesBuiltInConverter(property.PropertyType, options))
				return null;

			kinds[i] = kind;
			properties[i] = property;
		}

		return new DirectRowBinder
		{
			Kinds = kinds,
			Properties = properties,
			CreateObject = typeInfo.CreateObject,
			OnDeserializing = typeInfo.OnDeserializing,
			OnDeserialized = typeInfo.OnDeserialized
		};
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
			return options.TryGetTypeInfo(propertyType, out var propertyTypeInfo)
				&& propertyTypeInfo.Converter.GetType().Assembly == typeof(JsonSerializerOptions).Assembly;
		}
		catch
		{
			// Resolvers may throw for unregistered types - treat as ineligible and keep the slow path.
			return false;
		}
	}
}
