// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Runtime.CompilerServices;

using Elastic.Esql.Core;
using Elastic.Esql.Extensions;

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
	public static string ResolveFieldName(this Expression expression, JsonMetadataManager metadata)
	{
		expression = expression.UnwrapConvertExpressions();

		return expression switch
		{
			MethodCallExpression
			{
				Method.Name: "MultiField",
				Arguments: [var sourceExpression, ConstantExpression { Value: string multiField }]
			} mc
				when mc.Method.DeclaringType == typeof(GeneralPurposeExtensions) =>
				$"{sourceExpression.ResolveFieldName(metadata)}.{multiField}",
			MemberExpression member => ResolveMemberFieldPath(member, metadata),
			_ => throw new NotSupportedException($"Cannot extract field name from expression: {expression}")
		};
	}

	private static string ResolveMemberFieldPath(MemberExpression member, JsonMetadataManager metadata)
	{
		var segment = ResolveMemberSegmentName(member, metadata);
		var parent = member.Expression?.UnwrapConvertExpressions();

		return parent switch
		{
			ParameterExpression => segment,
			MemberExpression parentMember => $"{ResolveMemberFieldPath(parentMember, metadata)}.{segment}",
			_ => throw new NotSupportedException($"Cannot extract field name from expression: {member}")
		};
	}

	private static string ResolveMemberSegmentName(MemberExpression member, JsonMetadataManager metadata)
	{
		var declaringType = member.Member.DeclaringType
			?? throw new NotSupportedException($"Cannot extract field name from expression: {member}");

		return declaringType.IsDefined(typeof(CompilerGeneratedAttribute), false)
			? metadata.Options.PropertyNamingPolicy?.ConvertName(member.Member.Name) ?? member.Member.Name
			: metadata.ResolvePropertyName(declaringType, member.Member);
	}
}
