// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Runtime.CompilerServices;

using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.Formatting;

namespace Elastic.Esql.Translation;

internal static class TranslationExtensions
{
	/// <summary>
	/// Determines whether the expression is a constant/member-access chain that can be
	/// evaluated to a value (closure-rooted, or static when <paramref name="allowStaticRoot"/> is set).
	/// </summary>
	private static bool TerminatesInEvaluableRoot(Expression? expression, bool allowStaticRoot)
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

		// The chain walked off the end: a static member access.
		return allowStaticRoot;
	}

	/// <summary>
	/// Determines whether the expression can be evaluated to a constant value
	/// (closure-rooted or static member-access chains).
	/// </summary>
	public static bool SupportsEvaluation(this Expression expression) =>
		TerminatesInEvaluableRoot(expression, allowStaticRoot: true);

	/// <summary>
	/// Returns true when a member-access chain terminates in a <see cref="ConstantExpression"/>
	/// (a compiler-generated closure instance), meaning the chain can be evaluated to a value.
	/// Static chains terminate in null and keep their dedicated translations (e.g. NOW()).
	/// </summary>
	public static bool IsClosureRooted(this Expression? expression) =>
		TerminatesInEvaluableRoot(expression, allowStaticRoot: false);

	public static Expression UnwrapConvertExpressions(this Expression expression)
	{
		while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convertExpression)
			expression = convertExpression.Operand;

		return expression;
	}

	/// <summary>
	/// Resolves a field name from an expression, handling plain member access and <c>MultiField()</c> calls.
	/// Returned paths are ES|QL-escaped per segment via <see cref="EsqlIdentifier.EscapeColumnName"/>.
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
				$"{sourceExpression.ResolveFieldName(metadata)}.{EsqlIdentifier.EscapeColumnName(multiField)}",
			MemberExpression member => ResolveMemberFieldPath(member, metadata),
			_ => throw new NotSupportedException($"Cannot extract field name from expression: {expression}")
		};
	}

	private static string ResolveMemberFieldPath(MemberExpression member, JsonMetadataManager metadata)
	{
		// Escape only the leaf segment; parent segments from recursion are already escaped to avoid double-quoting composed paths.
		var segment = EsqlIdentifier.EscapeColumnName(ResolveMemberSegmentName(member, metadata));
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
