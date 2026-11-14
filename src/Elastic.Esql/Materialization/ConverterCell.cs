// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Reads a <see cref="DirectBinderKind.Converter"/> cell of one property and assigns it. Where the runtime supports
/// dynamic code, the cell reads through its contract typed for the property type and assigns through a setter compiled
/// for that type, so a value-type cell is not boxed. The non-generic
/// <see cref="JsonSerializer.Deserialize(ref Utf8JsonReader, JsonTypeInfo)"/> runs the same typed read and boxes its
/// result, so both give the same value and raise the same exceptions, and the compiled setter assigns what
/// <see cref="JsonPropertyInfo.Set"/> assigns under the modifier-free resolver chain.
/// </summary>
internal abstract class ConverterCell(JsonPropertyInfo property)
{
	// Bound through a delegate so an overload added to the method cannot make a name lookup ambiguous.
	private static readonly MethodInfo CreateDefinition =
		new Func<JsonPropertyInfo, JsonTypeInfo, ConverterCell>(Create<object>)
			.Method
			.GetGenericMethodDefinition();

	/// <summary>The property the cell assigns.</summary>
	protected JsonPropertyInfo Property { get; } = property;

	/// <summary>
	/// Deserializes the cell whose first token <paramref name="reader"/> is on and assigns it to
	/// <paramref name="instance"/>, or returns false for a value the serializer rejects.
	/// </summary>
	public abstract bool TryBind(ref Utf8JsonReader reader, object instance);

	/// <summary>Returns a cell typed for the property type, or an object cell when the runtime does not support dynamic code.</summary>
	[UnconditionalSuppressMessage(
		"AOT",
		"IL3050",
		Justification = "Create returns the object cell before any generic instantiation when dynamic code is unsupported, so Native AOT reads through it."
	)]
	[UnconditionalSuppressMessage(
		"Trimming",
		"IL2060",
		Justification = "Create<TValue> places no DynamicallyAccessedMembers requirement on TValue, so no instantiation needs members the trimmer could remove."
	)]
	public static ConverterCell Create(JsonPropertyInfo property, JsonTypeInfo cellTypeInfo)
	{
		if (!DynamicCode.IsSupported)
			return new ObjectConverterCell(property, cellTypeInfo);

		// Create<TValue> never returns null. A null-forgiving operator in place of the throw fails the netstandard2.0 build,
		// whose unannotated Invoke makes it an unnecessary suppression.
		return (ConverterCell?)CreateDefinition.MakeGenericMethod(property.PropertyType).Invoke(null, [property, cellTypeInfo])
			?? throw new InvalidOperationException($"No converter cell was created for {property.PropertyType}.");
	}

	private static ConverterCell Create<TValue>(JsonPropertyInfo property, JsonTypeInfo cellTypeInfo) =>
		new ConverterCell<TValue>(property, (JsonTypeInfo<TValue>)cellTypeInfo, DirectSetterCompiler.TryCreate<TValue>(property));

	// The serializer raises its nullability error for a null value here, and the direct path must not accept what it rejects.
	protected bool RejectsNull(JsonTypeInfo typeInfo) => typeInfo.Options.RespectNullableAnnotations && !Property.IsSetNullable;
}

/// <summary>
/// A <see cref="ConverterCell"/> for a property of type <typeparamref name="TValue"/>, assigning through
/// <see cref="JsonPropertyInfo.Set"/> where no setter could be compiled.
/// </summary>
internal sealed class ConverterCell<TValue>(JsonPropertyInfo property, JsonTypeInfo<TValue> typeInfo, Action<object, TValue>? setter) : ConverterCell(property)
{
	public override bool TryBind(ref Utf8JsonReader reader, object instance)
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

		if (value is null && RejectsNull(typeInfo))
			return false;

		if (setter is null)
			Property.Set!(instance, value);
		else
			setter(instance, value!);

		return true;
	}
}

/// <summary>
/// A <see cref="ConverterCell"/> for a runtime without dynamic code, which reads through the non-generic
/// <see cref="JsonSerializer.Deserialize(ref Utf8JsonReader, JsonTypeInfo)"/> and assigns through
/// <see cref="JsonPropertyInfo.Set"/>.
/// </summary>
internal sealed class ObjectConverterCell(JsonPropertyInfo property, JsonTypeInfo typeInfo) : ConverterCell(property)
{
	public override bool TryBind(ref Utf8JsonReader reader, object instance)
	{
		object? value;
		try
		{
			value = JsonSerializer.Deserialize(ref reader, typeInfo);
		}
		catch (JsonException)
		{
			return false;
		}

		if (value is null && RejectsNull(typeInfo))
			return false;

		Property.Set!(instance, value);
		return true;
	}
}
