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

	// Tracks pending GroupJoin for combining with subsequent SelectMany (left outer join pattern)
	private PendingGroupJoin? _pendingGroupJoin;

	private sealed record PendingGroupJoin(
		Expression InnerSource,
		LambdaExpression OuterKeySelector,
		LambdaExpression InnerKeySelector,
		LambdaExpression ResultSelector
	);

	public EsqlQueryProvider Provider { get; } = provider ?? throw new ArgumentNullException(nameof(provider));
	public EsqlTranslationContext Context { get; } = new() { FieldNameResolver = provider.FieldNameResolver, InlineParameters = inlineParameters };
	public string? DefaultIndexPattern { get; } = defaultIndexPattern;

	/// <summary>
	/// Translates a LINQ expression to an ES|QL query model.
	/// </summary>
	public EsqlQuery Translate(Expression expression)
	{
		_ = Visit(expression);

		if (_pendingGroupJoin is not null)
			throw new NotSupportedException("GroupJoin must be followed by SelectMany with DefaultIfEmpty() to form a left outer join pattern.");

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

			case nameof(Queryable.Join):
				VisitJoin(node);
				break;

			case nameof(Queryable.GroupJoin):
				VisitGroupJoin(node);
				break;

			case nameof(Queryable.SelectMany):
				VisitSelectMany(node);
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
			EmitProjectionCommands(result);
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
			EmitProjectionCommands(result);
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

		EmitJoinWithCollisionHandling(lookupIndex, onCondition, resultSelectorArg);
	}

	private void VisitJoin(MethodCallExpression node)
	{
		var lookupIndex = ExtractLookupIndex(node.Arguments[1].UnwrapConvertExpressions());
		var resultSelectorArg = node.Arguments[4];

		var outerField = ExtractFieldFromQuotedLambda(node.Arguments[2]);
		var innerField = ExtractFieldFromQuotedLambda(node.Arguments[3]);

		var onCondition = outerField == innerField
			? outerField
			: $"{outerField} == {innerField}";

		EmitJoinWithCollisionHandling(lookupIndex, onCondition, resultSelectorArg, whereNotNullField: innerField);
	}

	private void VisitGroupJoin(MethodCallExpression node)
	{
		if (_pendingGroupJoin is not null)
			throw new NotSupportedException("GroupJoin must be followed by SelectMany with DefaultIfEmpty() to form a left outer join pattern.");

		if (node.Arguments.Count < 5)
			throw new NotSupportedException("GroupJoin requires 5 arguments.");

		var innerSource = node.Arguments[1];
		var outerKeyArg = node.Arguments[2];
		var innerKeyArg = node.Arguments[3];
		var resultSelectorArg = node.Arguments[4];

		if (outerKeyArg is not UnaryExpression { Operand: LambdaExpression outerKey })
			throw new NotSupportedException("Expected a lambda expression for outer key selector.");

		if (innerKeyArg is not UnaryExpression { Operand: LambdaExpression innerKey })
			throw new NotSupportedException("Expected a lambda expression for inner key selector.");

		if (resultSelectorArg is not UnaryExpression { Operand: LambdaExpression resultSelector })
			throw new NotSupportedException("Expected a lambda expression for GroupJoin result selector.");

		_pendingGroupJoin = new PendingGroupJoin(innerSource, outerKey, innerKey, resultSelector);
	}

	private void VisitSelectMany(MethodCallExpression node)
	{
		if (_pendingGroupJoin is null)
			throw new NotSupportedException("SelectMany is only supported as part of a left outer join pattern (GroupJoin + SelectMany with DefaultIfEmpty).");

		if (node.Arguments.Count < 3)
			throw new NotSupportedException("SelectMany requires a collection selector and result selector for the join pattern.");

		var collectionSelectorArg = node.Arguments[1];
		var resultSelectorArg = node.Arguments[2];

		if (collectionSelectorArg is not UnaryExpression { Operand: LambdaExpression collectionSelector })
			throw new NotSupportedException("Expected a lambda expression for collection selector.");

		if (!IsDefaultIfEmptyCall(collectionSelector.Body))
			throw new NotSupportedException("SelectMany is only supported with DefaultIfEmpty() for the left outer join pattern.");

		var pending = _pendingGroupJoin;
		_pendingGroupJoin = null;

		var lookupIndex = ExtractLookupIndex(pending.InnerSource.UnwrapConvertExpressions());

		var outerField = ExtractFieldName(pending.OuterKeySelector.Body);
		var innerField = ExtractFieldName(pending.InnerKeySelector.Body);

		var onCondition = outerField == innerField
			? outerField
			: $"{outerField} == {innerField}";

		if (resultSelectorArg is UnaryExpression { Operand: LambdaExpression resultLambda }
			&& resultLambda.Body is not ParameterExpression)
		{
			var rewrittenLambda = RewriteGroupJoinResultSelector(pending.ResultSelector, resultLambda);

			// Wrap the rewritten lambda back into a UnaryExpression so EmitJoinWithCollisionHandling can unwrap it
			var quotedLambda = Expression.Quote(rewrittenLambda);
			EmitJoinWithCollisionHandling(lookupIndex, onCondition, quotedLambda);
		}
		else
		{
			Context.Commands.Add(new LookupJoinCommand(lookupIndex, onCondition));
		}
	}

	private static bool IsDefaultIfEmptyCall(Expression expression) =>
		expression is MethodCallExpression { Method.Name: "DefaultIfEmpty" };

	/// <summary>
	/// Rewrites the SelectMany result selector so it references outer/inner parameters directly,
	/// instead of going through the intermediate anonymous type created by GroupJoin.
	/// </summary>
	/// <remarks>
	/// GroupJoin produces <c>(outer, innerCollection) => new { c = outer, ps = innerCollection }</c>.
	/// SelectMany then has <c>(temp, p) => new { temp.c.Name, p.Price }</c>.
	/// This method rewrites the SelectMany lambda into <c>(outer, inner) => new { outer.Name, inner.Price }</c>
	/// so that <see cref="SelectProjectionVisitor"/> can process it identically to a <c>LeftJoin</c> result selector.
	/// </remarks>
	private static LambdaExpression RewriteGroupJoinResultSelector(LambdaExpression groupJoinResultSelector, LambdaExpression selectManyResultSelector)
	{
		if (groupJoinResultSelector.Body is not NewExpression groupJoinNew || groupJoinNew.Members is null)
			throw new NotSupportedException("GroupJoin result selector must create an anonymous type.");

		var groupJoinOuterParam = groupJoinResultSelector.Parameters[0];

		string? outerMemberName = null;
		for (var i = 0; i < groupJoinNew.Arguments.Count; i++)
		{
			if (groupJoinNew.Arguments[i] == groupJoinOuterParam)
			{
				outerMemberName = groupJoinNew.Members[i].Name;
				break;
			}
		}

		if (outerMemberName is null)
			throw new NotSupportedException("Could not identify the outer entity member in the GroupJoin result selector.");

		var selectManyTempParam = selectManyResultSelector.Parameters[0];
		var selectManyInnerParam = selectManyResultSelector.Parameters[1];

		var newOuterParam = Expression.Parameter(groupJoinOuterParam.Type, "outer");
		var newInnerParam = Expression.Parameter(selectManyInnerParam.Type, "inner");

		var rewriter = new GroupJoinResultRewriter(selectManyTempParam, outerMemberName, selectManyInnerParam, newOuterParam, newInnerParam);
		var rewrittenBody = rewriter.Visit(selectManyResultSelector.Body);

		return Expression.Lambda(rewrittenBody, newOuterParam, newInnerParam);
	}

	/// <summary>
	/// Replaces member accesses through the GroupJoin intermediate type with direct parameter references.
	/// <c>temp.c</c> becomes <c>outerParam</c> and <c>p</c> becomes <c>innerParam</c>.
	/// </summary>
	private sealed class GroupJoinResultRewriter(
		ParameterExpression tempParam,
		string outerMemberName,
		ParameterExpression originalInnerParam,
		ParameterExpression newOuterParam,
		ParameterExpression newInnerParam
	) : ExpressionVisitor
	{
		protected override Expression VisitMember(MemberExpression node)
		{
			if (node.Expression == tempParam && node.Member.Name == outerMemberName)
				return newOuterParam;

			return base.VisitMember(node);
		}

		protected override Expression VisitParameter(ParameterExpression node) =>
			node == originalInnerParam ? newInnerParam : base.VisitParameter(node);
	}

	/// <summary>
	/// Emits RENAME, EVAL, and KEEP commands in the correct order from a projection result.
	/// KEEP is always emitted to reduce the result set to only the projected fields.
	/// </summary>
	private void EmitProjectionCommands(SelectProjectionVisitor.ProjectionResult result)
	{
		if (result.RenameFields.Count > 0)
			Context.Commands.Add(new RenameCommand(result.RenameFields));

		if (result.EvalExpressions.Count > 0)
			Context.Commands.Add(new EvalCommand(result.EvalExpressions));

		var allKeepFields = new List<string>(result.KeepFields);
		foreach (var (_, target) in result.RenameFields)
			allKeepFields.Add(target);
		foreach (var evalExpr in result.EvalExpressions)
			allKeepFields.Add(evalExpr.Split('=')[0].Trim());

		if (allKeepFields.Count > 0)
			Context.Commands.Add(new KeepCommand(allKeepFields));
	}

	/// <summary>
	/// Shared join emission: detects field collisions, emits EVAL to preserve outer values,
	/// emits LOOKUP JOIN, and processes the result selector projection.
	/// </summary>
	private void EmitJoinWithCollisionHandling(
		string lookupIndex,
		string onCondition,
		Expression resultSelectorArg,
		string? whereNotNullField = null
	)
	{
		if (resultSelectorArg is not UnaryExpression { Operand: LambdaExpression resultLambda }
			|| resultLambda.Body is ParameterExpression)
		{
			// Identity projection — no collision handling needed (no KEEP to filter temps)
			Context.Commands.Add(new LookupJoinCommand(lookupIndex, onCondition));

			if (whereNotNullField is not null)
				Context.Commands.Add(new WhereCommand($"{whereNotNullField} IS NOT NULL"));

			return;
		}

		var innerType = resultLambda.Parameters[1].Type;
		var remappings = DetectJoinFieldCollisions(resultLambda, innerType);

		if (remappings is not null)
			EmitCollisionEval(remappings);

		Context.Commands.Add(new LookupJoinCommand(lookupIndex, onCondition));

		if (whereNotNullField is not null)
			Context.Commands.Add(new WhereCommand($"{whereNotNullField} IS NOT NULL"));

		var projectionVisitor = new SelectProjectionVisitor(Context);
		var result = remappings is not null
			? projectionVisitor.TranslateJoinProjection(resultLambda, resultLambda.Parameters[0], remappings)
			: projectionVisitor.Translate(resultLambda);

		var innerFieldNames = Context.GetAllFieldNames(innerType);
		EmitJoinProjectionCommands(result, innerFieldNames);
	}

	/// <summary>
	/// Emits projection commands after a join, converting renames to EVALs when the
	/// target name collides with an inner field that still exists post-join.
	/// ES|QL's RENAME fails if the target column already exists; EVAL overwrites it.
	/// </summary>
	private void EmitJoinProjectionCommands(SelectProjectionVisitor.ProjectionResult result, HashSet<string> innerFieldNames)
	{
		var safeRenames = new List<(string Source, string Target)>();
		var evalExpressions = new List<string>(result.EvalExpressions);

		foreach (var (source, target) in result.RenameFields)
		{
			if (innerFieldNames.Contains(target))
				evalExpressions.Add($"{target} = {source}");
			else
				safeRenames.Add((source, target));
		}

		if (safeRenames.Count > 0)
			Context.Commands.Add(new RenameCommand(safeRenames));

		if (evalExpressions.Count > 0)
			Context.Commands.Add(new EvalCommand(evalExpressions));

		var allKeepFields = new List<string>(result.KeepFields);
		foreach (var (_, target) in safeRenames)
			allKeepFields.Add(target);
		foreach (var evalExpr in evalExpressions)
			allKeepFields.Add(evalExpr.Split('=')[0].Trim());

		if (allKeepFields.Count > 0)
			Context.Commands.Add(new KeepCommand(allKeepFields));
	}

	/// <summary>
	/// Detects field name collisions between outer and inner types in a join result selector.
	/// Returns a remapping dictionary (originalField -> tempField) for colliding outer fields,
	/// or null if no collisions exist.
	/// </summary>
	private Dictionary<string, string>? DetectJoinFieldCollisions(LambdaExpression resultSelector, Type innerType)
	{
		var innerFieldNames = Context.GetAllFieldNames(innerType);

		if (innerFieldNames.Count == 0)
			return null;

		var outerParam = resultSelector.Parameters[0];
		var collector = new JoinFieldCollector(Context, outerParam);
		_ = collector.Visit(resultSelector.Body);

		Dictionary<string, string>? remappings = null;
		foreach (var outerField in collector.OuterFields)
		{
			if (!innerFieldNames.Contains(outerField))
				continue;

			remappings ??= new Dictionary<string, string>(StringComparer.Ordinal);
			remappings[outerField] = $"_esql_outer_{outerField}";
		}

		return remappings;
	}

	/// <summary>
	/// Emits <c>EVAL _esql_outer_x = x</c> for each colliding field to preserve outer values
	/// before the LOOKUP JOIN overwrites them.
	/// </summary>
	private void EmitCollisionEval(Dictionary<string, string> remappings)
	{
		var evalExprs = remappings
			.Select(kv => $"{kv.Value} = {kv.Key}")
			.ToList();
		Context.Commands.Add(new EvalCommand(evalExprs));
	}

	/// <summary>
	/// Walks a join result selector collecting resolved field names accessed from the outer parameter.
	/// </summary>
	private sealed class JoinFieldCollector(
		EsqlTranslationContext context,
		ParameterExpression outerParam
	) : ExpressionVisitor
	{
		public HashSet<string> OuterFields { get; } = new(StringComparer.Ordinal);

		protected override Expression VisitMember(MemberExpression node)
		{
			if (node.Expression is ParameterExpression param
				&& param == outerParam
				&& node.Member.DeclaringType is not null)
			{
				_ = OuterFields.Add(context.ResolveFieldName(node.Member.DeclaringType, node.Member));
			}

			return base.VisitMember(node);
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
				fieldNames.Add(selectorLambda.Body.ResolveFieldName(Context.FieldNameResolver));
		}
		return fieldNames;
	}

	private string ExtractFieldName(Expression expression) =>
		expression.ResolveFieldName(Provider.FieldNameResolver);
}
