// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Core;

namespace Elastic.Esql.Translation;

internal static class ExpressionTranslationHelpers
{
	public static bool IsRootedInParameter(MemberExpression member, ParameterExpression? expectedParameter = null)
	{
		Expression? current = member;
		while (current is MemberExpression memberExpression)
			current = memberExpression.Expression?.UnwrapConvertExpressions();

		if (current is not ParameterExpression parameter)
			return false;

		return expectedParameter is null || parameter == expectedParameter;
	}

	public static bool IsObjectSelectionType(Type type)
	{
		var candidateType = Nullable.GetUnderlyingType(type) ?? type;

		return !candidateType.IsValueType
			&& candidateType != typeof(string)
			&& candidateType != typeof(object)
			&& !TypeHelper.IsEnumerableType(candidateType);
	}

	public static List<MemberExpression> GetMemberChainFromRoot(MemberExpression member)
	{
		var chain = new List<MemberExpression>();
		Expression? current = member;

		while (current is MemberExpression memberExpression)
		{
			chain.Add(memberExpression);
			current = memberExpression.Expression?.UnwrapConvertExpressions();
		}

		chain.Reverse();
		return chain;
	}
}
