// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Elastic.Esql.Translation;

/// <summary>
/// Pre-processing visitor that merges consecutive <c>Select</c> calls into a single projection
/// by composing their lambdas at the expression tree level. This eliminates redundant
/// intermediate RENAME/EVAL/KEEP commands in the generated ES|QL.
/// </summary>
internal sealed class SelectMergingVisitor : ExpressionVisitor
{
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Expression tree construction for LINQ Select merging; types are statically known.")]
	[UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		var visited = (MethodCallExpression)base.VisitMethodCall(node);

		if (!IsSelectCall(visited))
			return visited;

		if (visited.Arguments[0] is not MethodCallExpression innerSelect || !IsSelectCall(innerSelect))
			return visited;

		if (innerSelect.Arguments[0] is MethodCallExpression { Method.Name: "GroupBy" })
			return visited;

		var innerLambda = ExtractLambda(innerSelect.Arguments[1]);
		var outerLambda = ExtractLambda(visited.Arguments[1]);

		if (innerLambda is null || outerLambda is null)
			return visited;

		var memberMap = BuildMemberMap(innerLambda);
		if (memberMap is null)
			return visited;

		var substitution = new MemberSubstitutionVisitor(outerLambda.Parameters[0], memberMap);
		var composedBody = substitution.Visit(outerLambda.Body);

		if (!substitution.Success)
			return visited;

		var composedLambda = Expression.Lambda(composedBody, innerLambda.Parameters[0]);

		var method = visited.Method.GetGenericMethodDefinition()
			.MakeGenericMethod(innerLambda.Parameters[0].Type, outerLambda.ReturnType);

		return Expression.Call(method, innerSelect.Arguments[0], Expression.Quote(composedLambda));
	}

	private static bool IsSelectCall(MethodCallExpression node) =>
		node.Method.Name == nameof(Queryable.Select) && node.Arguments.Count >= 2;

	private static LambdaExpression? ExtractLambda(Expression arg) =>
		arg is UnaryExpression { Operand: LambdaExpression lambda } ? lambda : null;

	/// <summary>
	/// Builds a map from the inner lambda's output members to their source expressions.
	/// Returns null if the body form is not supported for merging.
	/// </summary>
	private static Dictionary<MemberInfo, Expression>? BuildMemberMap(LambdaExpression lambda) =>
		lambda.Body switch
		{
			NewExpression { Members: not null } newExpr => BuildNewExpressionMap(newExpr),
			MemberInitExpression memberInit => BuildMemberInitMap(memberInit),
			_ => null
		};

	private static Dictionary<MemberInfo, Expression> BuildNewExpressionMap(NewExpression newExpr)
	{
		var map = new Dictionary<MemberInfo, Expression>(newExpr.Members!.Count);
		for (var i = 0; i < newExpr.Arguments.Count; i++)
			map[newExpr.Members[i]] = newExpr.Arguments[i];
		return map;
	}

	private static Dictionary<MemberInfo, Expression> BuildMemberInitMap(MemberInitExpression memberInit)
	{
		var map = new Dictionary<MemberInfo, Expression>(memberInit.Bindings.Count);
		foreach (var binding in memberInit.Bindings)
		{
			if (binding is MemberAssignment assignment)
				map[assignment.Member] = assignment.Expression;
		}
		return map;
	}

	/// <summary>
	/// Replaces member accesses on the target parameter with the corresponding expression
	/// from the member map. Sets <see cref="Success"/> to false if any access has no mapping.
	/// </summary>
	private sealed class MemberSubstitutionVisitor(
		ParameterExpression parameter,
		Dictionary<MemberInfo, Expression> memberMap
	) : ExpressionVisitor
	{
		public bool Success { get; private set; } = true;

		protected override Expression VisitMember(MemberExpression node)
		{
			if (node.Expression == parameter)
			{
				if (memberMap.TryGetValue(node.Member, out var replacement))
					return replacement;

				Success = false;
				return node;
			}

			return base.VisitMember(node);
		}
	}
}
