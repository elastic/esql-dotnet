// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Elastic.Esql.Translation;

/// <summary>
/// Handles a Select, Where or SelectMany whose lambda takes the element index, such as <c>Where((l, i) =&gt; ...)</c>.
/// The rows of an ES|QL query have no position to number, so a lambda that reads the index is refused. One that only
/// declares it goes on: a Select is turned into the Select without it, which the rest of the translation then reads as
/// usual, while Where and SelectMany are read through the body of their lambda, where the index has no part. The other
/// overloads with an index, those of TakeWhile, SkipWhile and SelectMany without a result selector, are left to the
/// translation, which refuses them with or without it.
/// </summary>
internal sealed class IndexedSelectVisitor : ExpressionVisitor
{
	private static readonly MethodInfo SelectDefinition =
		new Func<IQueryable<object>, Expression<Func<object, object>>, IQueryable<object>>(Queryable.Select).Method.GetGenericMethodDefinition();

	// Each overload by its definition, since a second int parameter alone may also be a joined element, as in the
	// result selector of SelectMany.
	private static readonly HashSet<MethodInfo> IndexedOverloads =
	[
		new Func<IQueryable<object>, Expression<Func<object, int, object>>, IQueryable<object>>(Queryable.Select).Method.GetGenericMethodDefinition(),
		new Func<IQueryable<object>, Expression<Func<object, int, bool>>, IQueryable<object>>(Queryable.Where).Method.GetGenericMethodDefinition(),
		new Func<IQueryable<object>, Expression<Func<object, int, IEnumerable<object>>>, Expression<Func<object, object, object>>, IQueryable<object>>(Queryable.SelectMany)
			.Method.GetGenericMethodDefinition()
	];

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	[UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		var visited = (MethodCallExpression)base.VisitMethodCall(node);

		if (!visited.Method.IsGenericMethod
			|| !IndexedOverloads.Contains(visited.Method.GetGenericMethodDefinition())
			|| visited.Arguments[1] is not UnaryExpression { Operand: LambdaExpression indexed })
			return visited;

		if (ExpressionTranslationHelpers.ReadsParameter(indexed.Body, indexed.Parameters[1]))
		{
			throw new NotSupportedException(
				$"{visited.Method.Name} with the element index is not supported: the rows of an ES|QL query have no position to number."
			);
		}

		if (visited.Method.Name != nameof(Queryable.Select))
			return visited;

		// The lambda keeps the type the Select declares: its body may be of a type derived from it, such as string for object.
		var select = SelectDefinition.MakeGenericMethod(visited.Method.GetGenericArguments());
		var lambdaType = select.GetParameters()[1].ParameterType.GetGenericArguments()[0];
		return Expression.Call(select, visited.Arguments[0], Expression.Quote(Expression.Lambda(lambdaType, indexed.Body, indexed.Parameters[0])));
	}
}
