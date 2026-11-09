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

	/// <summary>
	/// Whether the expression reads a lambda parameter: the one given, or any, leaving out those declared by a
	/// lambda inside the expression when asked to.
	/// </summary>
	public static bool ReadsParameter(Expression expression, ParameterExpression? parameter = null, bool skipOwnLambdas = false)
	{
		var finder = new ParameterFinder(parameter, skipOwnLambdas);
		_ = finder.Visit(expression);
		return finder.Found;
	}

	/// <summary>Replaces every occurrence of <paramref name="parameter"/> in the expression with <paramref name="replacement"/>.</summary>
	public static Expression? ReplaceParameter(Expression expression, ParameterExpression parameter, Expression replacement) =>
		new ParameterReplacer(parameter, replacement).Visit(expression);

	private sealed class ParameterFinder(ParameterExpression? parameter, bool skipOwnLambdas) : ExpressionVisitor
	{
		private readonly HashSet<ParameterExpression> _declared = [];

		public bool Found { get; private set; }

		protected override Expression VisitLambda<T>(Expression<T> node)
		{
			if (skipOwnLambdas)
				_declared.UnionWith(node.Parameters);

			return base.VisitLambda(node);
		}

		protected override Expression VisitParameter(ParameterExpression node)
		{
			Found |= (parameter is null || node == parameter) && !_declared.Contains(node);
			return base.VisitParameter(node);
		}
	}

	private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
	{
		protected override Expression VisitParameter(ParameterExpression node) =>
			node == parameter ? replacement : base.VisitParameter(node);
	}
}
