// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

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
			UnaryExpression { NodeType: ExpressionType.Convert } unary => ResolveUnary(unary),
			_ => throw new NotSupportedException($"Expression of type '{expression.GetType().Name}' ({expression.NodeType}) is not supported.")
		};
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

		return Convert.ChangeType(operandValue, underlyingTargetType, CultureInfo.CurrentCulture);
	}
}
