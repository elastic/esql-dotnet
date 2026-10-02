// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Elastic.Esql.Translation;

/// <summary>
/// Handles a Select whose selector takes the element index, <c>Select((l, i) =&gt; ...)</c>. The rows of an ES|QL query
/// have no position to number, so a selector that reads the index is refused; one that only declares it is turned into
/// the Select without it, which the rest of the translation then reads as usual.
/// </summary>
internal sealed class IndexedSelectVisitor : ExpressionVisitor
{
	private static readonly MethodInfo SelectDefinition =
		new Func<IQueryable<object>, Expression<Func<object, object>>, IQueryable<object>>(Queryable.Select).Method.GetGenericMethodDefinition();

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	[UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		var visited = (MethodCallExpression)base.VisitMethodCall(node);

		if (visited.Method.DeclaringType != typeof(Queryable) || visited.Method.Name != nameof(Queryable.Select)
			|| visited.Arguments[1] is not UnaryExpression { Operand: LambdaExpression { Parameters.Count: 2 } selector })
			return visited;

		if (new ParameterFinder(selector.Parameters[1]).IsReadIn(selector.Body))
		{
			throw new NotSupportedException(
				"Select with the element index is not supported: the rows of an ES|QL query have no position to number.");
		}

		var withoutIndex = Expression.Lambda(selector.Body, selector.Parameters[0]);
		return Expression.Call(
			SelectDefinition.MakeGenericMethod(selector.Parameters[0].Type, selector.ReturnType),
			visited.Arguments[0],
			Expression.Quote(withoutIndex));
	}

	private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
	{
		private bool _found;

		public bool IsReadIn(Expression expression)
		{
			_ = Visit(expression);
			return _found;
		}

		protected override Expression VisitParameter(ParameterExpression node)
		{
			_found |= node == parameter;
			return node;
		}
	}
}
