// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Elastic.Esql.Validation;

namespace Elastic.Esql.Translation;

internal static class ExpressionConstantResolver
{
	public static object? Resolve(Expression expression)
	{
		Verify.NotNull(expression);

		return expression switch
		{
			ConstantExpression constant => constant.Value,
			MemberExpression member => ResolveMember(member),
			UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary => ResolveUnary(unary),
			NewArrayExpression { NodeType: ExpressionType.NewArrayInit } newArray => ResolveNewArray(newArray),
			NewExpression newExpression => ResolveNew(newExpression),
			MemberInitExpression memberInit => ResolveMemberInit(memberInit),
			_ => throw new NotSupportedException($"Expression of type '{expression.GetType().Name}' ({expression.NodeType}) is not supported.")
		};
	}

	private static object? ResolveNew(NewExpression newExpression)
	{
		// NewExpression.Constructor is null only for default value-type construction (e.g. `new MyStruct()`),
		// which would require Activator.CreateInstance and is not AOT-safe. The supported callers
		// (KnnOptions and similar reference types) always carry a non-null Constructor.
		if (newExpression.Constructor is null)
			throw new NotSupportedException(
				$"Cannot resolve 'new {newExpression.Type.Name}()' without an explicit constructor. " +
				"Use a reference type or a constructor-bearing value type.");

		var args = new object?[newExpression.Arguments.Count];
		for (var i = 0; i < newExpression.Arguments.Count; i++)
			args[i] = Resolve(newExpression.Arguments[i]);

		return newExpression.Constructor.Invoke(args);
	}

	private static object? ResolveMemberInit(MemberInitExpression memberInit)
	{
		var instance = ResolveNew(memberInit.NewExpression)
			?? throw new InvalidOperationException(
				$"Cannot resolve constructor for '{memberInit.Type}' in MemberInitExpression.");

		foreach (var binding in memberInit.Bindings)
		{
			if (binding is not MemberAssignment assignment)
				throw new NotSupportedException($"MemberInit binding type '{binding.BindingType}' is not supported.");

			var value = Resolve(assignment.Expression);
			switch (assignment.Member)
			{
				case PropertyInfo property:
					property.SetValue(instance, value);
					break;
				case FieldInfo field:
					field.SetValue(instance, value);
					break;
				default:
					throw new NotSupportedException(
						$"MemberInit member '{assignment.Member.GetType().Name}' is not supported.");
			}
		}

		return instance;
	}

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Element type is statically referenced in the expression tree's NewArrayExpression.")]
	private static object? ResolveNewArray(NewArrayExpression newArray)
	{
		var elementType = newArray.Type.GetElementType()
			?? throw new NotSupportedException($"Array type '{newArray.Type}' has no element type.");

		var array = Array.CreateInstance(elementType, newArray.Expressions.Count);
		for (var i = 0; i < newArray.Expressions.Count; i++)
		{
			var value = Resolve(newArray.Expressions[i]);
			array.SetValue(value, i);
		}

		return array;
	}

	private static object? ResolveMember(MemberExpression member)
	{
		var instance = member.Expression is not null
			? Resolve(member.Expression)
			: null;

		return member.Member switch
		{
			FieldInfo field => field.GetValue(instance),
			PropertyInfo property => property.GetValue(instance),
			_ => throw new NotSupportedException(
				$"Member type '{member.Member.GetType().Name}' for member '{member.Member.Name}' is not supported.")
		};
	}

	private static object? ResolveUnary(UnaryExpression unary)
	{
		var operandValue = Resolve(unary.Operand);

		if (operandValue is null)
		{
			// `null` converts to `null` for reference types and nullable value types.
			if (!unary.Type.IsValueType || Nullable.GetUnderlyingType(unary.Type) is not null)
				return null;

			throw new InvalidOperationException($"Cannot convert null to non-nullable value type '{unary.Type}'.");
		}

		var targetType = unary.Type;

		// Unwrap Nullable<T> to get the underlying target type for conversion.
		var underlyingTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

		// If the value is already assignable to the target type, no conversion needed.
		// This handles reference conversions (upcasts, interface casts, etc.)
		if (targetType.IsAssignableFrom(operandValue.GetType()))
			return operandValue;

		// If a custom conversion method is specified (user-defined conversion operators), invoke it directly.
		if (unary.Method is not null)
			return unary.Method.Invoke(null, [operandValue]);

		return Convert.ChangeType(operandValue, underlyingTargetType, CultureInfo.InvariantCulture);
	}
}
