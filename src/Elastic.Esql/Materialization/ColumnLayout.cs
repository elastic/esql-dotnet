// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;

namespace Elastic.Esql.Materialization;

/// <summary>Raw JSON bytes captured for a single column value during phase-1 buffering.</summary>
internal readonly record struct ValueSlice(int Start, int Length, bool IsNull);

/// <summary>
/// A node in the pre-computed column tree. Leaf nodes (<see cref="ColumnIndex"/> >= 0) map to a
/// specific position in the ES|QL values array. Branch nodes (<see cref="ColumnIndex"/> == -1)
/// represent nested JSON objects whose children are written inside <c>{ ... }</c>.
/// </summary>
internal sealed class ColumnNode
{
	public required string PropertyName { get; init; }
	public byte[] PrefixBytes { get; init; } = [];
	public int ColumnIndex { get; init; } = -1;
	public bool IsCollection { get; init; }

	/// <summary>
	/// On the <c>_source</c> leaf, the members declared as read from the document: the leaf then writes
	/// each of them, not the document itself.
	/// </summary>
	public SourceBinding[]? SourceBindings { get; set; }

	public int BranchIndex { get; set; } = -1;
	public ColumnNode? Parent { get; set; }
	public List<ColumnNode>? Children { get; set; }
}

/// <summary>
/// Pre-computed column-to-nested-JSON mapping for a target type and a set of ES|QL columns.
/// Built once per query, reused for every row.
/// </summary>
internal sealed class ColumnLayout
{
	private const int DefaultMaxDepth = 64;

	/// <summary>Virtual root whose <see cref="ColumnNode.Children"/> are the top-level properties.</summary>
	public ColumnNode Root { get; }

	/// <summary>Total number of columns in the ES|QL response.</summary>
	public int ColumnCount { get; }

	/// <summary>Maximum nesting depth of the tree (1 = flat).</summary>
	public int MaxDepth { get; }

	/// <summary>Leaf node lookup by original ES|QL column index.</summary>
	public ColumnNode[] LeafNodesByColumnIndex { get; }

	/// <summary>Total count of non-root branch nodes (nested objects).</summary>
	public int BranchNodeCount { get; }

	/// <summary>Direct-binding fast path metadata for eligible flat layouts, or null when rows must use the assemble-and-deserialize path.</summary>
	public DirectRowBinder? DirectBinder { get; }

	/// <summary>
	/// The built-in kind a single-column read binds its cell as, or null when a one-column read of a scalar target assembles
	/// its cell. The kind is kept here because the plan is built on every read, and the resolver judgment behind it reads
	/// type attributes on each call.
	/// </summary>
	public DirectBinderKind? ScalarKind { get; private init; }

	private ColumnLayout(
		ColumnNode root,
		int columnCount,
		int maxDepth,
		ColumnNode[] leafNodesByColumnIndex,
		int branchNodeCount,
		DirectRowBinder? directBinder)
	{
		Root = root;
		ColumnCount = columnCount;
		MaxDepth = maxDepth;
		LeafNodesByColumnIndex = leafNodesByColumnIndex;
		BranchNodeCount = branchNodeCount;
		DirectBinder = directBinder;
	}

	/// <summary>The depth limit the serializer enforces under <paramref name="options"/>, where a limit of zero means the default.</summary>
	internal static int EffectiveMaxDepth(JsonSerializerOptions options) => options.MaxDepth > 0 ? options.MaxDepth : DefaultMaxDepth;

	/// <summary>
	/// Builds a <see cref="ColumnLayout"/> for the given columns and target type.
	/// Throws <see cref="JsonException"/> if the resolved nesting depth exceeds
	/// <see cref="JsonSerializerOptions.MaxDepth"/>.
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext.")]
	public static ColumnLayout Build(
		ReadOnlySpan<EsqlResponseReader.ColumnInfo> columns,
		Type targetType,
		JsonMetadataManager metadata,
		IReadOnlyList<SourceMember>? sourceMembers = null)
	{
		var options = metadata.Options;
		var columnCount = columns.Length;

		JsonTypeInfo? typeInfo;
		try
		{
			typeInfo = metadata.GetPropertyBasedTypeInfo(targetType);
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// No property-based metadata (custom converter, or the resolver has none for the
			// type): degrade to a flat layout - dotted column names stay single-segment and the
			// row deserializes through the type's own converter/contract instead.
			typeInfo = null;
		}

		var root = new ColumnNode { PropertyName = string.Empty };

		for (var i = 0; i < columnCount; i++)
		{
			var columnName = columns[i].Name;
			var segments = ResolvePathSegments(columnName, typeInfo, options);

			var isCollection = IsCollectionColumn(segments, typeInfo, options);
			InsertIntoTree(root, segments, i, isCollection);
		}

		if (sourceMembers is { Count: > 0 })
			BindSourceMembers(root, columns, sourceMembers, typeInfo);

		var maxDepth = ComputeMaxDepth(root);

		var effectiveMaxDepth = EffectiveMaxDepth(options);
		if (maxDepth > effectiveMaxDepth)
			throw new JsonException(
				$"ES|QL column nesting depth ({maxDepth}) exceeds the configured maximum depth ({effectiveMaxDepth}). " +
				"Increase JsonSerializerOptions.MaxDepth if deeper nesting is expected.");

		var leafNodesByColumnIndex = new ColumnNode[columnCount];
		var branchNodeCount = IndexNodes(root, leafNodesByColumnIndex, 0);

		// The binder is cached together with this layout in the reader's column layout cache, so
		// eligibility is decided once per (target type, column schema).
		var directBinder = branchNodeCount == 0 && maxDepth <= 1 && columnCount > 0 && typeInfo is not null
			&& sourceMembers is not { Count: > 0 }
			? DirectRowBinder.TryCreate(leafNodesByColumnIndex, columns, typeInfo, metadata)
			: null;

		// Taken after the target's contract resolved, so the parts consulted for it are locked and the answer holds for every
		// read the cached layout serves. A resolver outside the modifier-free chain can hand the nullable form a contract
		// whose wrapper holds a custom converter while the type under it keeps the stock one, which no converter check sees,
		// so a single column takes a built-in kind only where a row would bind directly. The table lookup goes first, so a
		// one-column object target skips a resolver judgment it does not need.
		var scalarKind = columnCount == 1 && DirectRowBinder.TryClassifyScalar(targetType, options, out var kind) && metadata.ResolverIsModifierFree
			? kind
			: (DirectBinderKind?)null;

		return new ColumnLayout(root, columnCount, maxDepth, leafNodesByColumnIndex, branchNodeCount, directBinder) { ScalarKind = scalarKind };
	}

	/// <summary>
	/// Resolves a potentially dot-notated column name into path segments by walking the type hierarchy.
	/// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> matches at any level
	/// take precedence over structural nesting.
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext.")]
	private static string[] ResolvePathSegments(string columnName, JsonTypeInfo? typeInfo, JsonSerializerOptions options)
	{
		if (typeInfo is null || !columnName.Contains('.'))
			return [columnName];

		if (HasPropertyWithJsonName(typeInfo, columnName))
			return [columnName];

		var dotIndex = columnName.IndexOf('.');
		var prefix = columnName[..dotIndex];
		var remainder = columnName[(dotIndex + 1)..];

		var nestedTypeInfo = FindNestedTypeInfo(typeInfo, prefix, options);
		if (nestedTypeInfo is null)
			return [columnName];

		var subSegments = ResolvePathSegments(remainder, nestedTypeInfo, options);

		var result = new string[subSegments.Length + 1];
		result[0] = prefix;
		subSegments.CopyTo(result, 1);
		return result;
	}

	/// <summary>
	/// Hands the declared members to the <c>_source</c> leaf, each with the name the row gives it and
	/// whether the target is a collection, so that a single object in the document still fills a list.
	/// </summary>
	private static void BindSourceMembers(
		ColumnNode root,
		ReadOnlySpan<EsqlResponseReader.ColumnInfo> columns,
		IReadOnlyList<SourceMember> sourceMembers,
		JsonTypeInfo? typeInfo)
	{
		var sourceIndex = -1;
		for (var i = 0; i < columns.Length; i++)
		{
			if (columns[i].Name == "_source")
				sourceIndex = i;
		}

		if (sourceIndex < 0 || root.Children is null)
			return;

		var leaf = root.Children.Find(child => child.ColumnIndex == sourceIndex);
		if (leaf is null)
			return;

		leaf.SourceBindings = [.. sourceMembers.Select(member => new SourceBinding(
			BuildPrefixBytes(member.Name),
			[.. member.Path],
			typeInfo is not null && IsCollectionMember(typeInfo, member.Name)))];
	}

	private static bool IsCollectionMember(JsonTypeInfo typeInfo, string jsonName)
	{
		foreach (var prop in typeInfo.Properties)
		{
			if (string.Equals(prop.Name, jsonName, StringComparison.Ordinal))
				return TypeHelper.IsEnumerableType(prop.PropertyType);
		}
		return false;
	}

	private static bool HasPropertyWithJsonName(JsonTypeInfo typeInfo, string jsonName)
	{
		foreach (var prop in typeInfo.Properties)
		{
			if (string.Equals(prop.Name, jsonName, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Finds the <see cref="JsonTypeInfo"/> for a property's type, returning null if the property
	/// doesn't exist, is a scalar/collection, or its type is not registered.
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext.")]
	private static JsonTypeInfo? FindNestedTypeInfo(JsonTypeInfo parentTypeInfo, string propertyJsonName, JsonSerializerOptions options)
	{
		foreach (var prop in parentTypeInfo.Properties)
		{
			if (!string.Equals(prop.Name, propertyJsonName, StringComparison.Ordinal))
				continue;

			var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

			if (TypeHelper.IsEnumerableType(propType))
				return null;

			// Property type not registered: treat the dotted column as flat instead of nested.
			return JsonMetadataManager.TryResolveTypeInfo(propType, options) is { Kind: JsonTypeInfoKind.Object } sub ? sub : null;
		}
		return null;
	}

	/// <summary>Determines if a resolved column path maps to a collection-typed property.</summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Type info resolution delegates to the user-provided JsonSerializerOptions/JsonSerializerContext.")]
	private static bool IsCollectionColumn(string[] segments, JsonTypeInfo? typeInfo, JsonSerializerOptions options)
	{
		if (typeInfo is null)
			return false;

		var currentTypeInfo = typeInfo;

		for (var i = 0; i < segments.Length; i++)
		{
			var segment = segments[i];
			JsonPropertyInfo? matchedProp = null;

			foreach (var prop in currentTypeInfo.Properties)
			{
				if (string.Equals(prop.Name, segment, StringComparison.Ordinal))
				{
					matchedProp = prop;
					break;
				}
			}

			if (matchedProp is null)
				return false;

			if (i == segments.Length - 1)
				return IsCollectionContract(
					matchedProp.PropertyType,
					JsonMetadataManager.TryResolveTypeInfo(matchedProp.PropertyType, options),
					matchedProp.CustomConverter,
					options
				);

			var propType = Nullable.GetUnderlyingType(matchedProp.PropertyType) ?? matchedProp.PropertyType;

			// Intermediate type not registered: the path cannot resolve to a collection column.
			if (JsonMetadataManager.TryResolveTypeInfo(propType, options) is not { Kind: JsonTypeInfoKind.Object } next)
				return false;

			currentTypeInfo = next;
		}

		return false;
	}

	/// <summary>Determines whether a bare single-valued cell for <paramref name="type"/> is wrapped in an array before its converter reads it.</summary>
	internal static bool IsCollectionContract(Type type, JsonTypeInfo? typeInfo, JsonConverter? propertyConverter, JsonSerializerOptions options)
	{
		// JsonArray's contract kind is None, but its converter takes only an array, so a bare cell still needs the wrap.
		if (typeof(JsonArray).IsAssignableFrom(type) || typeInfo?.Kind == JsonTypeInfoKind.Enumerable)
			return true;

		// A custom converter's contract kind is always None, so the token shape it reads is unknown and the CLR type
		// decides: a collection type gets the wrap.
		var converter = (propertyConverter is null ? null : JsonMetadataManager.ExpandConverter(propertyConverter, type, options)) ?? typeInfo?.Converter;
		return converter is not null && !JsonMetadataManager.IsBuiltInConverter(converter) && TypeHelper.IsEnumerableType(type);
	}

	private static void InsertIntoTree(ColumnNode parent, string[] segments, int columnIndex, bool isCollection)
	{
		var current = parent;

		for (var i = 0; i < segments.Length; i++)
		{
			var segment = segments[i];
			var isLeaf = i == segments.Length - 1;

			if (isLeaf)
			{
				current.Children ??= [];
				current.Children.Add(new ColumnNode
				{
					PropertyName = segment,
					PrefixBytes = BuildPrefixBytes(segment),
					ColumnIndex = columnIndex,
					IsCollection = isCollection,
					Parent = current
				});
			}
			else
			{
				current.Children ??= [];
				var existing = FindBranchChild(current.Children, segment);
				if (existing is null)
				{
					existing = new ColumnNode
					{
						PropertyName = segment,
						PrefixBytes = BuildPrefixBytes(segment),
						Parent = current
					};
					current.Children.Add(existing);
				}
				current = existing;
			}
		}
	}

	private static int IndexNodes(ColumnNode node, ColumnNode[] leafNodesByColumnIndex, int branchIndex)
	{
		if (node.ColumnIndex >= 0)
		{
			leafNodesByColumnIndex[node.ColumnIndex] = node;
			return branchIndex;
		}

		if (!string.IsNullOrEmpty(node.PropertyName))
		{
			node.BranchIndex = branchIndex;
			branchIndex++;
		}

		if (node.Children is null)
			return branchIndex;

		foreach (var child in node.Children)
			branchIndex = IndexNodes(child, leafNodesByColumnIndex, branchIndex);

		return branchIndex;
	}

	private static byte[] BuildPrefixBytes(string propertyName)
	{
		var encoded = JsonEncodedText.Encode(propertyName);
		var utf8 = encoded.EncodedUtf8Bytes;
		var prefix = new byte[utf8.Length + 3];
		prefix[0] = (byte)'"';
		utf8.CopyTo(prefix.AsSpan(1));
		prefix[utf8.Length + 1] = (byte)'"';
		prefix[utf8.Length + 2] = (byte)':';
		return prefix;
	}

	private static ColumnNode? FindBranchChild(List<ColumnNode> children, string name)
	{
		foreach (var child in children)
		{
			if (child.ColumnIndex == -1 && string.Equals(child.PropertyName, name, StringComparison.Ordinal))
				return child;
		}
		return null;
	}

	private static int ComputeMaxDepth(ColumnNode node)
	{
		if (node.Children is null or { Count: 0 })
			return 0;

		var max = 0;
		foreach (var child in node.Children)
		{
			var childDepth = child.ColumnIndex >= 0 ? 1 : 1 + ComputeMaxDepth(child);
			if (childDepth > max)
				max = childDepth;
		}
		return max;
	}

}
