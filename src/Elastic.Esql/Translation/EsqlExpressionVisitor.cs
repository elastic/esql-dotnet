// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Main visitor that translates LINQ expressions to ES|QL query model.
/// </summary>
internal sealed class EsqlExpressionVisitor(EsqlQueryProvider provider, string? defaultIndexPattern, bool inlineParameters) : ExpressionVisitor
{
	// Tracks pending GroupBy key selector for combining with subsequent Select
	private LambdaExpression? _pendingGroupByKeySelector;

	public EsqlQueryProvider Provider { get; } = provider ?? throw new ArgumentNullException(nameof(provider));
	public EsqlTranslationContext Context { get; } = new() { FieldMetadataResolver = provider.FieldMetadataResolver, InlineParameters = inlineParameters };
	public string? DefaultIndexPattern { get; } = defaultIndexPattern;

	/// <summary>
	/// Translates a LINQ expression to an ES|QL query model.
	/// </summary>
	public EsqlQuery Translate(Expression expression)
	{
		_ = Visit(expression);

		if (Context.ElementType is null)
			throw new InvalidOperationException("Failed to determine result type for the given expression.");

		return new EsqlQuery(Context.ElementType!, [.. Context.Commands], !Context.Parameters.HasParameters ? null : Context.Parameters);
	}

	protected override Expression VisitConstant(ConstantExpression node)
	{
		if (node.Value is IQueryable queryable)
		{
			Context.ElementType = queryable.ElementType;
			Context.Commands.Add(new FromCommand(DefaultIndexPattern ?? string.Empty));
			return node;
		}

		return base.VisitConstant(node);
	}

	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		// Visit the source first (builds the query from inside out).
		if (node.Arguments.Count > 0)
			_ = Visit(node.Arguments[0]);

		var methodName = node.Method.Name;

		switch (methodName)
		{
			case nameof(EsqlQueryableExtensions.From):
				VisitFrom(node);
				break;

			case nameof(Queryable.Where):
				VisitWhere(node);
				break;

			case nameof(Queryable.Select):
				VisitSelect(node);
				break;

			case nameof(Queryable.OrderBy):
				VisitOrderBy(node, descending: false);
				break;

			case nameof(Queryable.OrderByDescending):
				VisitOrderBy(node, descending: true);
				break;

			case nameof(Queryable.ThenBy):
				VisitThenBy(node, descending: false);
				break;

			case nameof(Queryable.ThenByDescending):
				VisitThenBy(node, descending: true);
				break;

			case nameof(Queryable.Take):
				VisitTake(node);
				break;

			case nameof(Queryable.Skip):
				// Skip is not directly supported in ES|QL
				// For now, we'll throw an informative exception
				throw new NotSupportedException(
					$"'{nameof(Queryable.Skip)}' is not directly supported in ES|QL. Use SORT with pagination instead.");

			case nameof(Queryable.First):
			case nameof(Queryable.FirstOrDefault):
			case nameof(EsqlQueryableExtensions.FirstAsync):
			case nameof(EsqlQueryableExtensions.FirstOrDefaultAsync):
				VisitFirst(node);
				break;

			case nameof(Queryable.Single):
			case nameof(Queryable.SingleOrDefault):
			case nameof(EsqlQueryableExtensions.SingleAsync):
			case nameof(EsqlQueryableExtensions.SingleOrDefaultAsync):
				VisitSingle(node);
				break;

			case nameof(Queryable.Count):
			case nameof(Queryable.LongCount):
			case nameof(EsqlQueryableExtensions.CountAsync):
				VisitCount(node);
				break;

			case nameof(Queryable.Sum):
				VisitAggregation(node, "SUM");
				break;

			case nameof(Queryable.Average):
				VisitAggregation(node, "AVG");
				break;

			case nameof(Queryable.Min):
				VisitAggregation(node, "MIN");
				break;

			case nameof(Queryable.Max):
				VisitAggregation(node, "MAX");
				break;

			case nameof(Queryable.Any):
			case nameof(EsqlQueryableExtensions.AnyAsync):
				VisitAny(node);
				break;

			case nameof(Queryable.GroupBy):
				VisitGroupBy(node);
				break;

			case nameof(Queryable.Distinct):
				// Distinct can be handled with STATS ... BY all fields
				throw new NotSupportedException(
					$"'{nameof(Queryable.Distinct)}' is not directly supported. Consider using '{nameof(Queryable.GroupBy)}' instead.");

			case nameof(EsqlQueryableExtensions.Keep):
				VisitKeep(node);
				break;

			case nameof(EsqlQueryableExtensions.Drop):
				VisitDrop(node);
				break;

			case nameof(EsqlQueryableExtensions.Row):
				VisitRow(node);
				break;

			case nameof(EsqlQueryableExtensions.Completion):
				VisitCompletion(node);
				break;

			case nameof(EsqlQueryableExtensions.LookupJoin):
			case nameof(EsqlQueryableExtensions.LeftJoin):
				VisitLookupJoin(node);
				break;
		}

		return node;
	}

	private void VisitWhere(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var predicate = node.Arguments[1];
		if (predicate is UnaryExpression { Operand: LambdaExpression lambda })
		{
			var whereVisitor = new WhereClauseVisitor(Context);
			var condition = whereVisitor.Translate(lambda.Body);
			Context.Commands.Add(new WhereCommand(condition));
		}
	}

	private void VisitSelect(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var selector = node.Arguments[1];
		if (selector is UnaryExpression { Operand: LambdaExpression lambda })
		{
			// Check if this Select follows a GroupBy (result selector for aggregations)
			if (_pendingGroupByKeySelector != null)
			{
				var groupByVisitor = new GroupByVisitor(Context);
				var statsCommand = groupByVisitor.Translate(_pendingGroupByKeySelector, lambda);
				Context.Commands.Add(statsCommand);
				_pendingGroupByKeySelector = null;
				return;
			}

			var projectionVisitor = new SelectProjectionVisitor(Context);
			var result = projectionVisitor.Translate(lambda);

			if (result.KeepFields.Count > 0)
				Context.Commands.Add(new KeepCommand(result.KeepFields));

			if (result.EvalExpressions.Count > 0)
				Context.Commands.Add(new EvalCommand(result.EvalExpressions));
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
			Context.Commands.Add(new SortCommand(new SortField(fieldName, descending)));
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

			// TODO: This looks broken...

			// Find the last SortCommand and add to it
			var existingSorts = Context.Commands.OfType<SortCommand>().ToList();
			if (existingSorts.Count > 0)
			{
				var lastSort = existingSorts.Last();
				var allFields = lastSort.Fields.ToList();
				allFields.Add(new SortField(fieldName, descending));

				// Remove the old sort and add a new combined one
				// (This is a simplification - in practice we'd modify the query model)
				Context.Commands.Add(new SortCommand(new SortField(fieldName, descending)));
			}
			else
				Context.Commands.Add(new SortCommand(new SortField(fieldName, descending)));
		}
	}

	private void VisitTake(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var countArg = node.Arguments[1];
		if (countArg is ConstantExpression constant && constant.Value is int count)
			Context.Commands.Add(new LimitCommand(count));
	}

	private void VisitFirst(MethodCallExpression node)
	{
		// Add WHERE clause if predicate provided
		if (node.Arguments.Count >= 2)
		{
			var predicate = node.Arguments[1];
			if (predicate is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
			{
				var whereVisitor = new WhereClauseVisitor(Context);
				var condition = whereVisitor.Translate(lambda.Body);
				Context.Commands.Add(new WhereCommand(condition));
			}
		}

		Context.Commands.Add(new LimitCommand(1));
	}

	private void VisitSingle(MethodCallExpression node)
	{
		// Add WHERE clause if predicate provided
		if (node.Arguments.Count >= 2)
		{
			var predicate = node.Arguments[1];
			if (predicate is UnaryExpression { Operand: LambdaExpression lambda })
			{
				var whereVisitor = new WhereClauseVisitor(Context);
				var condition = whereVisitor.Translate(lambda.Body);
				Context.Commands.Add(new WhereCommand(condition));
			}
		}

		// Limit to 2 to detect multiple results
		Context.Commands.Add(new LimitCommand(2));
	}

	private void VisitCount(MethodCallExpression node)
	{
		// Add WHERE clause if predicate provided
		if (node.Arguments.Count >= 2)
		{
			var predicate = node.Arguments[1];
			if (predicate is UnaryExpression { Operand: LambdaExpression lambda })
			{
				var whereVisitor = new WhereClauseVisitor(Context);
				var condition = whereVisitor.Translate(lambda.Body);
				Context.Commands.Add(new WhereCommand(condition));
			}
		}

		Context.Commands.Add(new StatsCommand(["count = COUNT(*)"]));
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
		Context.Commands.Add(new StatsCommand([$"{resultName} = {function}({fieldName})"]));
	}

	private void VisitAny(MethodCallExpression node)
	{
		// Add WHERE clause if predicate provided
		if (node.Arguments.Count >= 2)
		{
			var predicate = node.Arguments[1];
			if (predicate is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
			{
				var whereVisitor = new WhereClauseVisitor(Context);
				var condition = whereVisitor.Translate(lambda.Body);
				Context.Commands.Add(new WhereCommand(condition));
			}
		}

		Context.Commands.Add(new LimitCommand(1));
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

	private void VisitFrom(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			throw new NotSupportedException();

		var indexPatternExpression = node.Arguments[1];
		var indexPattern = ExpressionConstantResolver.Resolve(indexPatternExpression);

		if (indexPattern is not string indexPatternString)
			throw new NotSupportedException("The index pattern only supports string constants.");

		var from = Context.Commands.OfType<FromCommand>().Single();
		var i = Context.Commands.IndexOf(from);
		Context.Commands.RemoveAt(i);
		Context.Commands.Insert(i, new FromCommand(indexPatternString));
	}

	private void VisitKeep(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var arg = node.Arguments[1];

		// String params overload: Keep("field1", "field2")
		if (arg is ConstantExpression { Value: string[] fields })
		{
			Context.Commands.Add(new KeepCommand(fields));
			return;
		}

		// Lambda selector overload: Keep(l => l.Field1, l => l.Field2)
		if (arg is NewArrayExpression arrayExpr)
		{
			var fieldNames = ExtractFieldsFromSelectors(arrayExpr);
			Context.Commands.Add(new KeepCommand(fieldNames));
			return;
		}

		// Projection overload: Keep(l => new { l.Field1, Alias = l.Field2 })
		if (arg is UnaryExpression { Operand: LambdaExpression lambda })
		{
			var projectionVisitor = new SelectProjectionVisitor(Context);
			var result = projectionVisitor.Translate(lambda);

			if (result.EvalExpressions.Count > 0)
				Context.Commands.Add(new EvalCommand(result.EvalExpressions));

			// Combine direct keep fields and eval result names into a single KEEP
			var allKeepFields = new List<string>(result.KeepFields);
			foreach (var evalExpr in result.EvalExpressions)
			{
				var aliasName = evalExpr.Split('=')[0].Trim();
				allKeepFields.Add(aliasName);
			}

			if (allKeepFields.Count > 0)
				Context.Commands.Add(new KeepCommand(allKeepFields));
		}
	}

	private void VisitDrop(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var arg = node.Arguments[1];

		// String params overload: Drop("field1", "field2")
		if (arg is ConstantExpression { Value: string[] fields })
		{
			Context.Commands.Add(new DropCommand(fields));
			return;
		}

		// Lambda selector overload: Drop(l => l.Field1, l => l.Field2)
		if (arg is NewArrayExpression arrayExpr)
		{
			var fieldNames = ExtractFieldsFromSelectors(arrayExpr);
			Context.Commands.Add(new DropCommand(fieldNames));
		}
	}

	private void VisitRow(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var arg = node.Arguments[1];
		if (arg is not UnaryExpression { Operand: LambdaExpression lambda })
			throw new NotSupportedException("Row requires a lambda expression.");

		if (lambda.Body is not NewExpression newExpr)
			throw new NotSupportedException("Row lambda must return an anonymous object (new { ... }).");

		var expressions = new List<string>();
		for (var i = 0; i < newExpr.Arguments.Count; i++)
		{
			var name = newExpr.Members![i].Name;
			var value = ExpressionConstantResolver.Resolve(newExpr.Arguments[i]);
			var formatted = Context.GetValueOrParameterName(name, value);
			expressions.Add($"{name} = {formatted}");
		}

		// ROW is a source command — replace the default FROM
		var from = Context.Commands.OfType<FromCommand>().SingleOrDefault();
		if (from != null)
		{
			var idx = Context.Commands.IndexOf(from);
			Context.Commands.RemoveAt(idx);
			Context.Commands.Insert(idx, new RowCommand(expressions));
		}
		else
			Context.Commands.Add(new RowCommand(expressions));
	}

	private void VisitCompletion(MethodCallExpression node)
	{
		if (node.Arguments.Count < 4)
			return;

		var promptArg = node.Arguments[1];
		var inferenceIdArg = node.Arguments[2];
		var columnArg = node.Arguments[3];

		var inferenceId = ExpressionConstantResolver.Resolve(inferenceIdArg) as string
			?? throw new NotSupportedException("The inferenceId parameter must be a string constant.");

		var column = ExpressionConstantResolver.Resolve(columnArg) as string;

		// Lambda overload: Completion(l => l.Field, inferenceId, column)
		if (promptArg is UnaryExpression { Operand: LambdaExpression lambda })
		{
			var fieldName = ExtractFieldName(lambda.Body);
			Context.Commands.Add(new CompletionCommand(fieldName, inferenceId, column));
			return;
		}

		// String overload: Completion("fieldName", inferenceId, column)
		if (ExpressionConstantResolver.Resolve(promptArg) is string prompt)
			Context.Commands.Add(new CompletionCommand(prompt, inferenceId, column));
	}

	private void VisitLookupJoin(MethodCallExpression node)
	{
		var lookupIndex = ExtractLookupIndex(node.Arguments[1].UnwrapConvertExpressions());

		string onCondition;
		Expression resultSelectorArg;

		if (node.Arguments.Count == 5)
		{
			// Key-selector variant: args are [source, inner/index, outerKey, innerKey, resultSelector]
			var outerKeyArg = node.Arguments[2];
			var innerKeyArg = node.Arguments[3];
			resultSelectorArg = node.Arguments[4];

			var outerField = ExtractFieldFromQuotedLambda(outerKeyArg);
			var innerField = ExtractFieldFromQuotedLambda(innerKeyArg);

			onCondition = outerField == innerField
				? outerField
				: $"{outerField} == {innerField}";
		}
		else
		{
			// Predicate variant: args are [source, inner/index, onCondition, resultSelector]
			var predicateArg = node.Arguments[2];
			resultSelectorArg = node.Arguments[3];

			if (predicateArg is not UnaryExpression { Operand: LambdaExpression lambda })
				throw new NotSupportedException("The ON condition must be a lambda expression.");

			var whereVisitor = new WhereClauseVisitor(Context);
			onCondition = whereVisitor.Translate(lambda.Body);
		}

		Context.Commands.Add(new LookupJoinCommand(lookupIndex, onCondition));

		// Process result selector projection into EVAL/KEEP commands
		// Skip if the body is just a parameter reference (identity projection like (o, i) => o)
		if (resultSelectorArg is UnaryExpression { Operand: LambdaExpression resultLambda }
			&& resultLambda.Body is not ParameterExpression)
		{
			var projectionVisitor = new SelectProjectionVisitor(Context);
			var result = projectionVisitor.Translate(resultLambda);

			if (result.EvalExpressions.Count > 0)
				Context.Commands.Add(new EvalCommand(result.EvalExpressions));

			var allKeepFields = new List<string>(result.KeepFields);
			foreach (var evalExpr in result.EvalExpressions)
			{
				var aliasName = evalExpr.Split('=')[0].Trim();
				allKeepFields.Add(aliasName);
			}

			if (allKeepFields.Count > 0)
				Context.Commands.Add(new KeepCommand(allKeepFields));
		}
	}

	private string ExtractLookupIndex(Expression innerExpression)
	{
		if (innerExpression is ConstantExpression { Value: string indexName })
			return indexName;

		// Unwrap: if it's a ConstantExpression wrapping a queryable, use the queryable's expression
		if (innerExpression is ConstantExpression { Value: IQueryable innerQueryable })
			innerExpression = innerQueryable.Expression;

		var innerVisitor = new EsqlExpressionVisitor(Provider, null, inlineParameters);
		var innerQuery = innerVisitor.Translate(innerExpression);
		var from = innerQuery.From;

		if (from is null || string.IsNullOrEmpty(from.IndexPattern))
			throw new NotSupportedException("The lookup source must specify an index using '.From(\"index_name\")'.");

		if (innerQuery.Commands.Any(c => c is not FromCommand))
			throw new NotSupportedException("The lookup source must contain only a FROM command.");

		return from.IndexPattern;
	}

	private string ExtractFieldFromQuotedLambda(Expression arg)
	{
		if (arg is not UnaryExpression { Operand: LambdaExpression lambda })
			throw new NotSupportedException("Expected a lambda expression for key selector.");

		return ExtractFieldName(lambda.Body);
	}

	private List<string> ExtractFieldsFromSelectors(NewArrayExpression arrayExpr)
	{
		var fieldNames = new List<string>();
		foreach (var element in arrayExpr.Expressions)
		{
			if (element is UnaryExpression { Operand: LambdaExpression selectorLambda })
				fieldNames.Add(selectorLambda.Body.ResolveFieldName(Context.FieldMetadataResolver));
		}
		return fieldNames;
	}

	private string ExtractFieldName(Expression expression) =>
		expression.ResolveFieldName(Provider.FieldMetadataResolver);
}
