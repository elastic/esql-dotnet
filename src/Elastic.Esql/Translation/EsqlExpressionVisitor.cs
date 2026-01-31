// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Core;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;
using Elastic.Esql.TypeMapping;

namespace Elastic.Esql.Translation;

/// <summary>
/// Main visitor that translates LINQ expressions to ES|QL query model.
/// </summary>
public class EsqlExpressionVisitor(EsqlQueryContext context) : ExpressionVisitor
{
	private readonly EsqlQueryContext _context = context ?? throw new ArgumentNullException(nameof(context));
	private EsqlQuery _query = new();

	// Tracks pending GroupBy key selector for combining with subsequent Select
	private LambdaExpression? _pendingGroupByKeySelector;

	/// <summary>
	/// Translates a LINQ expression to an ES|QL query model.
	/// </summary>
	public EsqlQuery Translate(Expression expression)
	{
		_query = new EsqlQuery();
		_ = Visit(expression);
		return _query;
	}

	protected override Expression VisitConstant(ConstantExpression node)
	{
		// This is typically the root queryable
		if (node.Value is IQueryable queryable)
		{
			var elementType = queryable.ElementType;
			_query.ElementType = elementType;

			// Use explicit index pattern from context if set, otherwise get from attribute or use type name
			var indexPattern = _context.IndexPattern
				?? FieldNameResolver.GetIndexPattern(elementType)
				?? ToIndexName(elementType.Name);

			_query.AddCommand(new FromCommand(indexPattern));
		}

		return base.VisitConstant(node);
	}

	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		// Visit the source first (builds the query from inside out)
		if (node.Arguments.Count > 0)
			_ = Visit(node.Arguments[0]);

		var methodName = node.Method.Name;

		switch (methodName)
		{
			case "Where":
				VisitWhere(node);
				break;

			case "Select":
				VisitSelect(node);
				break;

			case "OrderBy":
				VisitOrderBy(node, descending: false);
				break;

			case "OrderByDescending":
				VisitOrderBy(node, descending: true);
				break;

			case "ThenBy":
				VisitThenBy(node, descending: false);
				break;

			case "ThenByDescending":
				VisitThenBy(node, descending: true);
				break;

			case "Take":
				VisitTake(node);
				break;

			case "Skip":
				// Skip is not directly supported in ES|QL
				// For now, we'll throw an informative exception
				throw new NotSupportedException(
					"Skip is not directly supported in ES|QL. Use SORT with pagination instead.");

			case "First":
			case "FirstOrDefault":
				VisitFirst(node);
				break;

			case "Single":
			case "SingleOrDefault":
				VisitSingle(node);
				break;

			case "Count":
			case "LongCount":
				VisitCount(node);
				break;

			case "Sum":
				VisitAggregation(node, "SUM");
				break;

			case "Average":
				VisitAggregation(node, "AVG");
				break;

			case "Min":
				VisitAggregation(node, "MIN");
				break;

			case "Max":
				VisitAggregation(node, "MAX");
				break;

			case "Any":
				VisitAny(node);
				break;

			case "GroupBy":
				VisitGroupBy(node);
				break;

			case "Distinct":
				// Distinct can be handled with STATS ... BY all fields
				throw new NotSupportedException(
					"Distinct is not directly supported. Consider using GroupBy instead.");
		}

		return node;
	}

	private void VisitWhere(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var predicate = node.Arguments[1];
		if (predicate is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
		{
			var whereVisitor = new WhereClauseVisitor(_context);
			var condition = whereVisitor.Translate(lambda.Body);
			_query.AddCommand(new WhereCommand(condition));
		}
	}

	private void VisitSelect(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var selector = node.Arguments[1];
		if (selector is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
		{
			// Check if this Select follows a GroupBy (result selector for aggregations)
			if (_pendingGroupByKeySelector != null)
			{
				var groupByVisitor = new GroupByVisitor(_context);
				var statsCommand = groupByVisitor.Translate(_pendingGroupByKeySelector, lambda);
				_query.AddCommand(statsCommand);
				_pendingGroupByKeySelector = null;
				return;
			}

			var projectionVisitor = new SelectProjectionVisitor(_context);
			var result = projectionVisitor.Translate(lambda);

			if (result.KeepFields.Count > 0)
				_query.AddCommand(new KeepCommand(result.KeepFields));

			if (result.EvalExpressions.Count > 0)
				_query.AddCommand(new EvalCommand(result.EvalExpressions));
		}
	}

	private void VisitOrderBy(MethodCallExpression node, bool descending)
	{
		if (node.Arguments.Count < 2)
			return;

		var keySelector = node.Arguments[1];
		if (keySelector is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
		{
			var fieldName = ExtractFieldName(lambda.Body);
			_query.AddCommand(new SortCommand(new SortField(fieldName, descending)));
		}
	}

	private void VisitThenBy(MethodCallExpression node, bool descending)
	{
		if (node.Arguments.Count < 2)
			return;

		var keySelector = node.Arguments[1];
		if (keySelector is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
		{
			var fieldName = ExtractFieldName(lambda.Body);

			// Find the last SortCommand and add to it
			var existingSorts = _query.SortCommands.ToList();
			if (existingSorts.Count > 0)
			{
				var lastSort = existingSorts.Last();
				var allFields = lastSort.Fields.ToList();
				allFields.Add(new SortField(fieldName, descending));

				// Remove the old sort and add a new combined one
				// (This is a simplification - in practice we'd modify the query model)
				_query.AddCommand(new SortCommand(new SortField(fieldName, descending)));
			}
			else
				_query.AddCommand(new SortCommand(new SortField(fieldName, descending)));
		}
	}

	private void VisitTake(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var countArg = node.Arguments[1];
		if (countArg is ConstantExpression constant && constant.Value is int count)
			_query.AddCommand(new LimitCommand(count));
	}

	private void VisitFirst(MethodCallExpression node)
	{
		// Add WHERE clause if predicate provided
		if (node.Arguments.Count >= 2)
		{
			var predicate = node.Arguments[1];
			if (predicate is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
			{
				var whereVisitor = new WhereClauseVisitor(_context);
				var condition = whereVisitor.Translate(lambda.Body);
				_query.AddCommand(new WhereCommand(condition));
			}
		}

		_query.AddCommand(new LimitCommand(1));
	}

	private void VisitSingle(MethodCallExpression node)
	{
		// Add WHERE clause if predicate provided
		if (node.Arguments.Count >= 2)
		{
			var predicate = node.Arguments[1];
			if (predicate is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
			{
				var whereVisitor = new WhereClauseVisitor(_context);
				var condition = whereVisitor.Translate(lambda.Body);
				_query.AddCommand(new WhereCommand(condition));
			}
		}

		// Limit to 2 to detect multiple results
		_query.AddCommand(new LimitCommand(2));
	}

	private void VisitCount(MethodCallExpression node)
	{
		// Add WHERE clause if predicate provided
		if (node.Arguments.Count >= 2)
		{
			var predicate = node.Arguments[1];
			if (predicate is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
			{
				var whereVisitor = new WhereClauseVisitor(_context);
				var condition = whereVisitor.Translate(lambda.Body);
				_query.AddCommand(new WhereCommand(condition));
			}
		}

		_query.AddCommand(new StatsCommand(["count = COUNT(*)"]));
	}

	private void VisitAggregation(MethodCallExpression node, string function)
	{
		var fieldName = "*";

		if (node.Arguments.Count >= 2)
		{
			var selector = node.Arguments[1];
			if (selector is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
				fieldName = ExtractFieldName(lambda.Body);
		}

		var resultName = function.ToLowerInvariant();
		_query.AddCommand(new StatsCommand([$"{resultName} = {function}({fieldName})"]));
	}

	private void VisitAny(MethodCallExpression node)
	{
		// Add WHERE clause if predicate provided
		if (node.Arguments.Count >= 2)
		{
			var predicate = node.Arguments[1];
			if (predicate is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
			{
				var whereVisitor = new WhereClauseVisitor(_context);
				var condition = whereVisitor.Translate(lambda.Body);
				_query.AddCommand(new WhereCommand(condition));
			}
		}

		_query.AddCommand(new LimitCommand(1));
	}

	private void VisitGroupBy(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var keySelector = node.Arguments[1];
		if (keySelector is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
		{
			// Store the key selector for combining with subsequent Select
			_pendingGroupByKeySelector = lambda;
			// Don't add command yet - wait for Select to combine into STATS...BY
		}
	}

	private string ExtractFieldName(Expression expression) =>
		expression switch
		{
			MemberExpression member => _context.FieldNameResolver.Resolve(member.Member),
			UnaryExpression { Operand: MemberExpression innerMember } => _context.FieldNameResolver.Resolve(innerMember.Member),
			_ => throw new NotSupportedException($"Cannot extract field name from expression: {expression}")
		};

	private static string ToIndexName(string typeName)
	{
		// Convert PascalCase to kebab-case with wildcard
		var result = new System.Text.StringBuilder();
		for (var i = 0; i < typeName.Length; i++)
		{
			var c = typeName[i];
			if (char.IsUpper(c) && i > 0)
				_ = result.Append('-');
			_ = result.Append(char.ToLowerInvariant(c));
		}
		return result.ToString() + "*";
	}
}
