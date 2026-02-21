// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Extensions;
using Elastic.Esql.FieldMetadataResolver;

namespace Elastic.Esql.Translation;

internal static class TranslationExtensions
{
	public static bool SupportsEvaluation(this Expression expression)
	{
		var current = expression;

		while (current is not null)
		{
			switch (current)
			{
				case ConstantExpression:
					// Closure-rooted constant.
					return true;
				case MemberExpression member:
					current = member.Expression;
					break;
				case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert:
					current = convert.Operand;
					break;
				default:
					// Unsupported expression types like method calls, parameter expressions, etc.
					return false;
			}
		}

		// Static member access => not closure-rooted, but we allow evaluation.
		return true;
	}

	public static Expression UnwrapConvertExpressions(this Expression expression)
	{
		while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convertExpression)
			expression = convertExpression.Operand;

		return expression;
	}

	/// <summary>
	/// Resolves a field name from an expression, handling plain member access and <c>MultiField()</c> calls.
	/// </summary>
	public static string ResolveFieldName(this Expression expression, IEsqlFieldMetadataResolver resolver)
	{
		expression = expression.UnwrapConvertExpressions();

		return expression switch
		{
			MethodCallExpression { Method.Name: "MultiField" } mc
				when mc.Method.DeclaringType == typeof(GeneralPurposeExtensions) =>
				$"{mc.Arguments[0].ResolveFieldName(resolver)}.{(string)((ConstantExpression)mc.Arguments[1]).Value!}",
			MemberExpression member =>
				resolver.GetFieldName(member.Member.DeclaringType!, member.Member),
			_ => throw new NotSupportedException($"Cannot extract field name from expression: {expression}")
		};
	}
}
