// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Compiles strongly typed property setters for the direct-binding fast path so value-type cells are assigned
/// without boxing through <see cref="JsonPropertyInfo.Set"/>. Only used where the runtime can generate code,
/// so Native AOT keeps the object-typed setter. Only a public setter or a public field gets one, because a public
/// member is the only kind a source-generated context always reaches, and for a member it cannot reach the context
/// emits a <see cref="JsonPropertyInfo.Set"/> that throws, as the serializer does.
/// </summary>
/// <remarks>
/// The compiled setter assigns the member directly and bypasses <see cref="JsonPropertyInfo.Set"/>, so the binder asks
/// for one only when the resolver cannot carry a contract modifier that replaces it.
/// </remarks>
internal static class DirectSetterCompiler
{
	/// <summary>
	/// Returns a compiled <c>Action&lt;object, TValue&gt;</c> for the exact value type of <paramref name="kind"/>, for the
	/// property's own list type on a string list or for the underlying integer type of the enum on an enum, or null when
	/// the runtime does not support dynamic code, the declaring type is a struct, the member cannot be resolved via
	/// <see cref="JsonPropertyInfo.AttributeProvider"/>, or the member is not a property with a public setter or a public
	/// writable field.
	/// </summary>
	public static Delegate? TryCreate(JsonPropertyInfo property, DirectBinderKind kind)
	{
		if (!DynamicCode.IsSupported)
			return null;

		return kind switch
		{
			DirectBinderKind.String => TryCreate<string?>(property),
			DirectBinderKind.Bool => TryCreate<bool>(property),
			DirectBinderKind.Int32 => TryCreate<int>(property),
			DirectBinderKind.Int64 => TryCreate<long>(property),
			DirectBinderKind.Double => TryCreate<double>(property),
			DirectBinderKind.Single => TryCreate<float>(property),
			DirectBinderKind.Decimal => TryCreate<decimal>(property),
			DirectBinderKind.DateTime => TryCreate<DateTime>(property),
			DirectBinderKind.DateTimeOffset => TryCreate<DateTimeOffset>(property),
			DirectBinderKind.Guid => TryCreate<Guid>(property),
			DirectBinderKind.Enum => Type.GetTypeCode(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) switch
			{
				TypeCode.SByte => TryCreate<sbyte>(property),
				TypeCode.Byte => TryCreate<byte>(property),
				TypeCode.Int16 => TryCreate<short>(property),
				TypeCode.UInt16 => TryCreate<ushort>(property),
				TypeCode.Int32 => TryCreate<int>(property),
				TypeCode.UInt32 => TryCreate<uint>(property),
				TypeCode.Int64 => TryCreate<long>(property),
				TypeCode.UInt64 => TryCreate<ulong>(property),
				_ => null
			},
			DirectBinderKind.StringList when property.PropertyType == typeof(string[]) => TryCreate<string[]>(property),
			DirectBinderKind.StringList => TryCreate<List<string>>(property),
			_ => null
		};
	}

	private static Action<object, TValue>? TryCreate<TValue>(JsonPropertyInfo property)
	{
		var instance = Expression.Parameter(typeof(object), "instance");
		var value = Expression.Parameter(typeof(TValue), "value");

		MemberExpression target;
		// A struct instance would be unboxed by copy inside the lambda and the assignment lost.
		switch (property.AttributeProvider)
		{
			case PropertyInfo { SetMethod.IsPublic: true, DeclaringType: { IsValueType: false } propertyDeclaringType } propertyInfo:
				target = Expression.Property(Expression.Convert(instance, propertyDeclaringType), propertyInfo);
				break;
			case FieldInfo { IsPublic: true, IsInitOnly: false, DeclaringType: { IsValueType: false } fieldDeclaringType } fieldInfo:
				target = Expression.Field(Expression.Convert(instance, fieldDeclaringType), fieldInfo);
				break;
			default:
				return null;
		}

		// The binder classifies Nullable<T> by its underlying type and an enum by its underlying integer type, so the cell
		// value may need lifting or converting to the enum.
		Expression assigned = property.PropertyType == typeof(TValue) ? value : Expression.Convert(value, property.PropertyType);

		return Expression.Lambda<Action<object, TValue>>(Expression.Assign(target, assigned), instance, value).Compile();
	}
}
