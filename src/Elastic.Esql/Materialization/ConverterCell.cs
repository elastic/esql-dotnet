// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Reads a <see cref="DirectBinderKind.Converter"/> cell through its contract typed for the property type and assigns
/// it through a setter compiled for that type, so a value-type cell is not boxed. The non-generic
/// <see cref="JsonSerializer.Deserialize(ref Utf8JsonReader, JsonTypeInfo)"/> runs the same typed read and boxes its
/// result, so both give the same value and raise the same exceptions, and the compiled setter assigns what
/// <see cref="JsonPropertyInfo.Set"/> assigns under the modifier-free resolver chain.
/// </summary>
internal abstract class ConverterCell
{
	// Bound through a delegate so an overload added to the method cannot make a name lookup ambiguous.
	private static readonly MethodInfo CreateDefinition =
		new Func<JsonPropertyInfo, JsonTypeInfo, ConverterCell>(Create<object>)
			.Method
			.GetGenericMethodDefinition();

	/// <summary>
	/// Deserializes the cell whose first token <paramref name="reader"/> is on and assigns it to
	/// <paramref name="instance"/>, or returns false for a value the serializer rejects.
	/// </summary>
	public abstract bool TryBind(ref Utf8JsonReader reader, JsonPropertyInfo property, object instance);

	/// <summary>Returns a cell typed for the property type, or null when the runtime does not support dynamic code.</summary>
	[UnconditionalSuppressMessage(
		"AOT",
		"IL3050",
		Justification = "TryCreate returns null before any generic instantiation when dynamic code is unsupported, and Native AOT keeps the object path."
	)]
	[UnconditionalSuppressMessage(
		"Trimming",
		"IL2060",
		Justification = "Create<TValue> places no DynamicallyAccessedMembers requirement on TValue, so no instantiation needs members the trimmer could remove."
	)]
	public static ConverterCell? TryCreate(JsonPropertyInfo property, JsonTypeInfo cellTypeInfo)
	{
		if (!DynamicCode.IsSupported)
			return null;

		return (ConverterCell?)CreateDefinition.MakeGenericMethod(property.PropertyType).Invoke(null, [property, cellTypeInfo]);
	}

	private static ConverterCell Create<TValue>(JsonPropertyInfo property, JsonTypeInfo cellTypeInfo) =>
		new ConverterCell<TValue>((JsonTypeInfo<TValue>)cellTypeInfo, DirectSetterCompiler.TryCreate<TValue>(property));
}

/// <summary>
/// A <see cref="ConverterCell"/> for a property of type <typeparamref name="TValue"/>, assigning through
/// <see cref="JsonPropertyInfo.Set"/> where no setter could be compiled.
/// </summary>
internal sealed class ConverterCell<TValue>(JsonTypeInfo<TValue> typeInfo, Action<object, TValue>? setter) : ConverterCell
{
	public override bool TryBind(ref Utf8JsonReader reader, JsonPropertyInfo property, object instance)
	{
		TValue? value;
		try
		{
			value = JsonSerializer.Deserialize(ref reader, typeInfo);
		}
		catch (JsonException)
		{
			return false;
		}

		// The serializer raises its nullability error for this value, and the direct path must not accept what it rejects.
		if (value is null && typeInfo.Options.RespectNullableAnnotations && !property.IsSetNullable)
			return false;

		if (setter is null)
			property.Set!(instance, value);
		else
			setter(instance, value!);

		return true;
	}
}
