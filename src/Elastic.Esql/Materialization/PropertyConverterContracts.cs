// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Builds the contract that reads a cell through its property's own converter. The contract is built around the root
/// options because the converter must receive them exactly as the serializer hands them over, which a copy of the
/// options with the converter inserted would not give.
/// </summary>
internal static class PropertyConverterContracts
{
	// Bound through a delegate so an overload added to the method cannot make a name lookup ambiguous.
	private static readonly MethodInfo CreateValueInfoDefinition =
		new Func<JsonSerializerOptions, JsonConverter, JsonTypeInfo<object>>(JsonMetadataServices.CreateValueInfo<object>)
			.Method
			.GetGenericMethodDefinition();

	/// <summary>Returns a contract that reads <paramref name="propertyType"/> through <paramref name="converter"/>, or null when none can be built.</summary>
	public static JsonTypeInfo? TryCreate(Type propertyType, JsonConverter converter, JsonSerializerOptions options)
	{
		if (!DynamicCode.IsSupported)
			return null;

		if (JsonMetadataManager.ExpandConverter(converter, propertyType, options) is not { } expanded || expanded is JsonConverterFactory)
			return null;

		converter = expanded;

		// The serializer wraps a converter for T around a T? property when it builds the contract. A converter of any
		// other type reaches the property through the serializer's casting converter, so the slow path handles it the
		// way the serializer does.
		if (converter.Type != propertyType)
			return null;

		// A value contract carries no collection or object metadata, so it serves only a converter of the value kind: a
		// built-in value converter, or a custom converter, alone or inside the built-in nullable wrapper, which takes its
		// kind from the converter it wraps and calls that converter directly. A built-in value converter keeps a value
		// contract of its own, because one converter type can carry other settings, such as the string enum converter
		// beside the numeric one. A structured converter reads against the metadata of the type's own contract. When
		// its kind differs from that contract's, the own contract carries a converter of the other kind, so only the
		// serializer can pair the property's converter with that metadata. With matching kinds, the converter reads its
		// elements through the root options' metadata, which the own contract carries, so the own contract stands in
		// when its converter has the same runtime type.
		var own = JsonMetadataManager.IsBuiltInConverter(converter) ? JsonMetadataManager.TryResolveTypeInfo(propertyType, options) : null;

		if (TryCreateValueContract(propertyType, converter, options) is not { } valueContract)
			return null;

		if (own is null || valueContract.Kind == JsonTypeInfoKind.None)
			return valueContract;

		if (valueContract.Kind != own.Kind)
			return null;

		return own.Converter.GetType() == converter.GetType() ? own : null;
	}

	[UnconditionalSuppressMessage(
		"AOT",
		"IL3050",
		Justification = "TryCreate returns null before any generic instantiation when dynamic code is unsupported, and Native AOT keeps the slow path for the type."
	)]
	[UnconditionalSuppressMessage(
		"Trimming",
		"IL2060",
		Justification = "CreateValueInfo<T> places no DynamicallyAccessedMembers requirement on T, so no instantiation depends on members the trimmer could remove."
	)]
	private static JsonTypeInfo? TryCreateValueContract(Type propertyType, JsonConverter converter, JsonSerializerOptions options)
	{
		try
		{
			return (JsonTypeInfo?)CreateValueInfoDefinition.MakeGenericMethod(propertyType).Invoke(null, [options, converter]);
		}
		catch (TargetInvocationException)
		{
			return null;
		}
	}
}
