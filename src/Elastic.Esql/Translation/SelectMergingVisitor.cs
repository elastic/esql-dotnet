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
			if (TryResolveMemberChain(node, out var replacement))
				return replacement;

			if (IsRootedInParameter(node))
			{
				Success = false;
				return node;
			}

			return base.VisitMember(node);
		}

		private bool TryResolveMemberChain(MemberExpression node, out Expression replacement)
		{
			replacement = null!;

			var memberChain = new Stack<MemberInfo>();
			Expression? current = node;
			while (current is MemberExpression member)
			{
				memberChain.Push(member.Member);
				current = member.Expression?.UnwrapConvertExpressions();
			}

			if (current != parameter || memberChain.Count == 0)
				return false;

			var first = memberChain.Pop();
			if (!memberMap.TryGetValue(first, out var resolved))
				return false;

			while (memberChain.Count > 0)
			{
				var nextMember = memberChain.Pop();
				if (!TryResolveMemberOnExpression(resolved, nextMember, out resolved))
					return false;
			}

			replacement = resolved;
			return true;
		}

		private static bool TryResolveMemberOnExpression(Expression source, MemberInfo member, out Expression resolved)
		{
			source = source.UnwrapConvertExpressions();

			if (TryResolveFromInitializer(source, member, out resolved))
				return true;

			if (TryCreateMemberAccess(source, member, out resolved))
				return true;

			resolved = null!;
			return false;
		}

		private static bool TryResolveFromInitializer(Expression source, MemberInfo member, out Expression resolved)
		{
			if (source is NewExpression { Members: not null } newExpression)
			{
				for (var i = 0; i < newExpression.Members.Count; i++)
				{
					var mappedMember = newExpression.Members[i];
					if (mappedMember == member || string.Equals(mappedMember.Name, member.Name, StringComparison.Ordinal))
					{
						resolved = newExpression.Arguments[i];
						return true;
					}
				}
			}

			if (source is MemberInitExpression memberInitExpression)
			{
				foreach (var binding in memberInitExpression.Bindings)
				{
					if (binding is not MemberAssignment assignment)
						continue;

					if (assignment.Member == member || string.Equals(assignment.Member.Name, member.Name, StringComparison.Ordinal))
					{
						resolved = assignment.Expression;
						return true;
					}
				}
			}

			resolved = null!;
			return false;
		}

		private static bool TryCreateMemberAccess(Expression source, MemberInfo member, out Expression resolved)
		{
			if (member.DeclaringType?.IsAssignableFrom(source.Type) == true)
			{
				resolved = Expression.MakeMemberAccess(source, member);
				return true;
			}

			resolved = null!;
			return false;
		}

		private bool IsRootedInParameter(MemberExpression node)
		{
			Expression? current = node;
			while (current is MemberExpression member)
				current = member.Expression?.UnwrapConvertExpressions();

			return current == parameter;
		}
	}
}
