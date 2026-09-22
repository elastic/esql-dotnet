// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Compiles strongly typed property setters for the direct-binding fast path so value-type cells are assigned
/// without boxing through <see cref="JsonPropertyInfo.Set"/>. Only used where the runtime can generate code;
/// Native AOT keeps the object-typed setter.
/// </summary>
/// <remarks>
/// The compiled setter assigns the member directly, so a contract modifier that replaced
/// <see cref="JsonPropertyInfo.Set"/> with custom logic is not honored on this path.
/// </remarks>
internal static class DirectSetterCompiler
{
	// netstandard2.0 has no RuntimeFeature.IsDynamicCodeSupported; every runtime it targets here compiles expression trees.
#if NETSTANDARD2_0
	private const bool DynamicCodeSupported = true;
#else
	private static bool DynamicCodeSupported => RuntimeFeature.IsDynamicCodeSupported;
#endif

	/// <summary>
	/// Returns a compiled <c>Action&lt;object, TValue&gt;</c> for the exact value type of <paramref name="kind"/>,
	/// or null when the runtime does not support dynamic code, the declaring type is a struct, or the member
	/// cannot be resolved via <see cref="JsonPropertyInfo.AttributeProvider"/>.
	/// </summary>
	public static Delegate? TryCreate(JsonPropertyInfo property, DirectBinderKind kind, Type declaringType)
	{
		// A struct instance would be unboxed by copy inside the lambda and the assignment lost.
		if (!DynamicCodeSupported || declaringType.IsValueType)
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
			_ => null
		};
	}

	private static Action<object, TValue>? TryCreate<TValue>(JsonPropertyInfo property)
	{
		var instance = Expression.Parameter(typeof(object), "instance");
		var value = Expression.Parameter(typeof(TValue), "value");

		MemberExpression target;
		switch (property.AttributeProvider)
		{
			case PropertyInfo { SetMethod: not null, DeclaringType: { } propertyDeclaringType } propertyInfo:
				target = Expression.Property(Expression.Convert(instance, propertyDeclaringType), propertyInfo);
				break;
			case FieldInfo { IsInitOnly: false, DeclaringType: { } fieldDeclaringType } fieldInfo:
				target = Expression.Field(Expression.Convert(instance, fieldDeclaringType), fieldInfo);
				break;
			default:
				return null;
		}

		// The binder classifies Nullable<T> by its underlying type, so the cell value may need lifting.
		Expression assigned = property.PropertyType == typeof(TValue) ? value : Expression.Convert(value, property.PropertyType);

		return Expression.Lambda<Action<object, TValue>>(Expression.Assign(target, assigned), instance, value).Compile();
	}
}
