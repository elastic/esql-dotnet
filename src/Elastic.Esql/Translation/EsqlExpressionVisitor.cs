// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Main visitor that translates LINQ expressions to ES|QL query model.
/// </summary>
internal sealed class EsqlExpressionVisitor(EsqlQueryProvider provider, bool inlineParameters) : ExpressionVisitor
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
	public EsqlTranslationContext Context { get; } = new() { Metadata = provider.Metadata, InlineParameters = inlineParameters };

#pragma warning disable IDE0032
	private ProjectionCommandEmitter? _projectionEmitter;
#pragma warning restore IDE0032
	private ProjectionCommandEmitter ProjectionEmitter => _projectionEmitter ??= new ProjectionCommandEmitter(Context);

#pragma warning disable IDE0032
	private JoinTranslator? _joinTranslator;
#pragma warning restore IDE0032
	private JoinTranslator JoinTranslator => _joinTranslator ??= new JoinTranslator(Context, ProjectionEmitter);

	/// <summary>
	/// Translates a LINQ expression to an ES|QL query model.
	/// </summary>
	public EsqlQuery Translate(Expression expression)
	{
		expression = new SelectMergingVisitor().Visit(expression);
		_ = Visit(expression);

		if (_pendingGroupJoin is not null)
			throw new NotSupportedException("GroupJoin must be followed by SelectMany with DefaultIfEmpty() to form a left outer join pattern.");

		if (_pendingGroupByKeySelector is not null)
			throw new NotSupportedException(
				"GroupBy must be followed by a Select that projects the group key and aggregations, " +
				"e.g. '.GroupBy(x => x.Field).Select(g => new { g.Key, Count = g.Count() })'.");

		if (Context.ElementType is null)
			throw new InvalidOperationException("Failed to determine result type for the given expression.");

		return new EsqlQuery(
			Context.ElementType,
			[.. Context.Commands],
			!Context.Parameters.HasParameters ? null : Context.Parameters,
			Context.QueryOptions,
			Context.ExecutorOptions
		);
	}

	protected override Expression VisitConstant(ConstantExpression node)
	{
		if (node.Value is IQueryable queryable)
		{
			Context.ElementType = queryable.ElementType;
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
		var declaringType = node.Method.DeclaringType;
		var isQueryableMethod = declaringType == typeof(Queryable);
		var isEsqlExtensionMethod = declaringType == typeof(EsqlQueryableExtensions);

		switch (methodName)
		{
			case nameof(EsqlQueryableExtensions.From) when isEsqlExtensionMethod:
				VisitFrom(node);
				break;

			case nameof(Queryable.Where) when isQueryableMethod:
				VisitWhere(node);
				break;

			case nameof(Queryable.Select) when isQueryableMethod:
				VisitSelect(node);
				break;

			case nameof(Queryable.OrderBy) when isQueryableMethod:
				VisitOrderBy(node, descending: false);
				break;

			case nameof(Queryable.OrderByDescending) when isQueryableMethod:
				VisitOrderBy(node, descending: true);
				break;

			case nameof(Queryable.ThenBy) when isQueryableMethod:
				VisitThenBy(node, descending: false);
				break;

			case nameof(Queryable.ThenByDescending) when isQueryableMethod:
				VisitThenBy(node, descending: true);
				break;

			case nameof(Queryable.Take) when isQueryableMethod:
				VisitTake(node);
				break;

			case nameof(Queryable.Skip) when isQueryableMethod:
				// Skip is not directly supported in ES|QL
				// For now, we'll throw an informative exception
				throw new NotSupportedException(
					$"'{nameof(Queryable.Skip)}' is not directly supported in ES|QL. Use SORT with pagination instead.");

			case nameof(Queryable.First) when isQueryableMethod:
			case nameof(Queryable.FirstOrDefault) when isQueryableMethod:
			case nameof(EsqlQueryableExtensions.FirstAsync) when isEsqlExtensionMethod:
			case nameof(EsqlQueryableExtensions.FirstOrDefaultAsync) when isEsqlExtensionMethod:
				VisitFirst(node);
				break;

			case nameof(Queryable.Single) when isQueryableMethod:
			case nameof(Queryable.SingleOrDefault) when isQueryableMethod:
			case nameof(EsqlQueryableExtensions.SingleAsync) when isEsqlExtensionMethod:
			case nameof(EsqlQueryableExtensions.SingleOrDefaultAsync) when isEsqlExtensionMethod:
				VisitSingle(node);
				break;

			case nameof(Queryable.Count) when isQueryableMethod:
			case nameof(Queryable.LongCount) when isQueryableMethod:
			case nameof(EsqlQueryableExtensions.CountAsync) when isEsqlExtensionMethod:
				VisitCount(node);
				break;

			case nameof(Queryable.Sum) when isQueryableMethod:
				VisitAggregation(node, "SUM");
				break;

			case nameof(Queryable.Average) when isQueryableMethod:
				VisitAggregation(node, "AVG");
				break;

			case nameof(Queryable.Min) when isQueryableMethod:
				VisitAggregation(node, "MIN");
				break;

			case nameof(Queryable.Max) when isQueryableMethod:
				VisitAggregation(node, "MAX");
				break;

			case nameof(Queryable.Any) when isQueryableMethod:
			case nameof(EsqlQueryableExtensions.AnyAsync) when isEsqlExtensionMethod:
				VisitAny(node);
				break;

			case nameof(Queryable.GroupBy) when isQueryableMethod:
				VisitGroupBy(node);
				break;

			case nameof(Queryable.Distinct) when isQueryableMethod:
				// Distinct can be handled with STATS ... BY all fields
				throw new NotSupportedException(
					$"'{nameof(Queryable.Distinct)}' is not directly supported. Consider using '{nameof(Queryable.GroupBy)}' instead.");

			case nameof(EsqlQueryableExtensions.Keep) when isEsqlExtensionMethod:
				VisitKeep(node);
				break;

			case nameof(EsqlQueryableExtensions.Drop) when isEsqlExtensionMethod:
				VisitDrop(node);
				break;

			case nameof(EsqlQueryableExtensions.Row) when isEsqlExtensionMethod:
				VisitRow(node);
				break;

			case nameof(EsqlQueryableExtensions.Completion) when isEsqlExtensionMethod:
				VisitCompletion(node);
				break;

			case nameof(EsqlQueryableExtensions.RawEsql) when isEsqlExtensionMethod:
				VisitRawEsql(node);
				break;

			case nameof(EsqlQueryableExtensions.LookupJoin) when isEsqlExtensionMethod:
			case nameof(EsqlQueryableExtensions.LeftJoin) when isEsqlExtensionMethod:
			case "LeftJoin" when isQueryableMethod:
				VisitLookupJoin(node);
				break;

			case nameof(EsqlQueryableExtensions.Fork) when isEsqlExtensionMethod:
				VisitFork(node);
				break;

			case nameof(EsqlQueryableExtensions.Fuse) when isEsqlExtensionMethod:
				VisitFuse(node);
				break;

			case nameof(Queryable.Join) when isQueryableMethod:
				VisitJoin(node);
				break;

			case nameof(Queryable.GroupJoin) when isQueryableMethod:
				VisitGroupJoin(node);
				break;

			case nameof(Queryable.SelectMany) when isQueryableMethod:
				VisitSelectMany(node);
				break;

			case nameof(Queryable.AsQueryable) when isQueryableMethod:
				// Transparent query shape conversion; no ES|QL command impact.
				break;

			default:
				// Options-carrying extension methods are defined by downstream executor
				// implementations (e.g. Elastic.Clients.Esql) with their own concrete options
				// types, so the core translator matches on the marker attribute rather than
				// method names or declaring types.
				if (node.Method.IsDefined(typeof(EsqlQueryOptionsMethodAttribute), inherit: false))
				{
					VisitQueryOptions(node);
					break;
				}

				throw new NotSupportedException($"Method '{declaringType?.Name}.{methodName}' is not supported in ES|QL translation.");
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
				ClearMetadataAfterStats();
				return;
			}

			var projectionVisitor = new SelectProjectionVisitor(Context);
			var result = projectionVisitor.Translate(lambda);
			ProjectionEmitter.Emit(result);
		}
	}

	private void VisitOrderBy(MethodCallExpression node, bool descending)
	{
		if (node.Arguments.Count < 2)
			return;

		var keySelector = node.Arguments[1];
		if (keySelector is UnaryExpression unary && unary.Operand is LambdaExpression lambda)
		{
			var fieldName = ExtractSortExpression(lambda.Body);
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
			var fieldName = ExtractSortExpression(lambda.Body);

			if (Context.Commands.Count > 0 && Context.Commands[^1] is SortCommand lastSort)
			{
				var allFields = lastSort.Fields.ToList();
				allFields.Add(new SortField(fieldName, descending));
				Context.Commands[^1] = new SortCommand(allFields);
			}
			else
				Context.Commands.Add(new SortCommand(new SortField(fieldName, descending)));
		}
	}

	private string ExtractSortExpression(Expression expression)
	{
		expression = expression.UnwrapConvertExpressions();

		// EsqlMetadata.X marker access -> emit underscore-prefixed identifier.
		if (expression is MemberExpression { Expression: null, Member: { } metaMember }
			&& metaMember.DeclaringType == typeof(EsqlMetadata))
			return Context.ResolveMetadataMemberOrThrow(metaMember.Name);

		if (expression is MethodCallExpression methodCall && methodCall.Method.DeclaringType != typeof(GeneralPurposeExtensions))
		{
			var translated = EsqlFunctionTranslator.TryTranslateMethodCall(methodCall, ExtractSortExpression);
			return translated ?? throw new NotSupportedException(
				$"Method {methodCall.Method.DeclaringType?.Name}.{methodCall.Method.Name} is not supported in ORDER BY.");
		}

		if (expression.SupportsEvaluation())
			return Context.FormatValue(ExpressionConstantResolver.Resolve(expression));

		return expression.ResolveFieldName(Context.Metadata);
	}

	private void VisitTake(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		var countArg = node.Arguments[1];
		if (countArg is ConstantExpression constant && constant.Value is int count)
			Context.Commands.Add(new LimitCommand(count));
		else if (ExpressionConstantResolver.Resolve(countArg) is int resolved)
			Context.Commands.Add(new LimitCommand(resolved));
		else
			throw new NotSupportedException(
				"'Take' with a non-int count (e.g. the 'Take(Range)' overload) is not supported in ES|QL. Use 'Take(int)' instead.");
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
		ClearMetadataAfterStats();
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
		ClearMetadataAfterStats();
	}

	/// <summary>
	/// After a STATS command, document metadata fields are no longer accessible to subsequent
	/// commands per ES|QL semantics. Clear active metadata to reflect that.
	/// </summary>
	private void ClearMetadataAfterStats()
	{
		Context.ActiveMetadata = MetadataField.None;
		Context.ForkActive = false;
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

		Context.Commands.Add(new StatsCommand(["result = COUNT(*)"]));
		Context.Commands.Add(new EvalCommand("result = result > 0"));
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

		var metadata = MetadataField.None;
		if (node.Arguments.Count >= 3)
		{
			var resolved = ExpressionConstantResolver.Resolve(node.Arguments[2]);
			if (resolved is MetadataField flags)
				metadata = flags;
		}

		if (Context.Commands.OfType<SourceCommand>().Any())
			throw new InvalidOperationException("A source command (FROM or ROW) already exists.");

		Context.Commands.Insert(0, new FromCommand(indexPatternString, metadata));
		Context.ActiveMetadata = metadata;
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
			ProjectionEmitter.Emit(result);
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
		var members = newExpr.Members ?? throw new NotSupportedException("Row lambda must provide named members.");
		for (var i = 0; i < newExpr.Arguments.Count; i++)
		{
			var name = members[i].Name;
			var value = ExpressionConstantResolver.Resolve(newExpr.Arguments[i]);
			var formatted = Context.GetValueOrParameterName(name, value);
			expressions.Add($"{name} = {formatted}");
		}

		if (Context.Commands.OfType<SourceCommand>().Any())
			throw new InvalidOperationException("A source command (FROM or ROW) already exists.");

		Context.Commands.Insert(0, new RowCommand(expressions));
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

	private void VisitRawEsql(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		if (ExpressionConstantResolver.Resolve(node.Arguments[1]) is not string rawEsql)
			throw new NotSupportedException("RawEsql requires a string fragment.");

		var fragments = NormalizeRawFragments(rawEsql);
		foreach (var fragment in fragments)
			Context.Commands.Add(new RawFragmentCommand(fragment));

		Context.ElementType = ResolveQueryableElementType(node.Method.ReturnType) ?? Context.ElementType;
	}

	private void VisitQueryOptions(MethodCallExpression node)
	{
		if (node.Arguments.Count < 2)
			return;

		// The core WithOptions overload carries the typed protocol options; downstream executors
		// define their own overloads with executor-specific types, so dispatch on the constant's
		// type. Each slot may be set only once per chain, so guard the target slot independently.
		var value = ExpressionConstantResolver.Resolve(node.Arguments[1]);

		if (value is EsqlQueryOptions queryOptions)
		{
			if (Context.QueryOptions is not null)
				throw new InvalidOperationException(
					$"Query options were already set earlier in this query chain; '{node.Method.Name}' can only be called once per query.");

			Context.QueryOptions = queryOptions;
		}
		else
		{
			if (Context.ExecutorOptions is not null)
				throw new InvalidOperationException(
					$"Query options were already set earlier in this query chain; '{node.Method.Name}' can only be called once per query.");

			Context.ExecutorOptions = value;
		}
	}

	private void VisitFork(MethodCallExpression node)
	{
		if (Context.InsideForkBranch)
			throw new InvalidOperationException(
				"Nested 'Fork' is not supported: a 'Fork' command cannot appear inside another fork's branch lambda.");

		if (Context.Commands.OfType<ForkCommand>().Any())
			throw new InvalidOperationException(
				"Only one 'Fork' command is supported per query (per the ES|QL spec).");

		if (node.Arguments.Count < 2)
			throw new NotSupportedException("Fork requires at least one branch.");

		var branchesArg = ExpressionConstantResolver.Resolve(node.Arguments[1]);
		if (branchesArg is not Array branchesArray || branchesArray.Length == 0)
			throw new NotSupportedException("Fork requires at least one branch.");

		var elementType = Context.ElementType
			?? throw new InvalidOperationException("Fork must follow a typed source command (FROM or ROW).");

		var branchFragments = new List<IReadOnlyList<string>>(branchesArray.Length);
		var inheritedMetadata = Context.ActiveMetadata;

		for (var i = 0; i < branchesArray.Length; i++)
		{
			if (branchesArray.GetValue(i) is not LambdaExpression branchLambda)
				throw new NotSupportedException($"Fork branch {i + 1} must be a lambda expression.");

			var fragments = ForkBranchVisitor.Translate(Provider, branchLambda, elementType, inheritedMetadata, inlineParameters, Context);
			if (fragments.Count == 0)
				throw new NotSupportedException($"Fork branch {i + 1} produced no commands.");

			branchFragments.Add(fragments);
		}

		Context.Commands.Add(new ForkCommand(branchFragments));
		Context.ForkActive = true;
		_lastForkBranchCount = branchesArray.Length;
	}

	private int _lastForkBranchCount;

	private void VisitFuse(MethodCallExpression node)
	{
		// Fuse parameters: (source, method, rankConstant, normalizer, weights, score, group, key)
		Debug.Assert(node.Arguments.Count == 8, "Fuse extension method always passes 8 arguments.");

		var method = (FuseMethod)(ExpressionConstantResolver.Resolve(node.Arguments[1]) ?? FuseMethod.Rrf);
		var rankConstant = ExpressionConstantResolver.Resolve(node.Arguments[2]) as int?;
		var normalizer = (ScoreNormalizer)(ExpressionConstantResolver.Resolve(node.Arguments[3]) ?? ScoreNormalizer.None);
		var weights = ExpressionConstantResolver.Resolve(node.Arguments[4]) as double[];
		var scoreLambda = ExpressionConstantResolver.Resolve(node.Arguments[5]) as LambdaExpression;
		var groupLambda = ExpressionConstantResolver.Resolve(node.Arguments[6]) as LambdaExpression;
		var keyLambda = ExpressionConstantResolver.Resolve(node.Arguments[7]) as LambdaExpression;

		ValidateFuseFollowsFork(weights);

		var scoreColumn = scoreLambda is not null ? ResolveSingleColumnFromLambda(scoreLambda, nameof(EsqlQueryableExtensions.Fuse), "score") : null;
		var groupColumn = groupLambda is not null ? ResolveSingleColumnFromLambda(groupLambda, nameof(EsqlQueryableExtensions.Fuse), "group") : null;
		var keyColumns = keyLambda is not null ? ResolveKeyColumnsFromLambda(keyLambda) : null;

		Context.Commands.Add(new FuseCommand(
			method: method,
			rankConstant: rankConstant,
			normalizer: normalizer,
			weights: weights,
			scoreColumn: scoreColumn,
			groupColumn: groupColumn,
			keyColumns: keyColumns));

		// Once Fuse merges the fork branches, the _fork discriminator is consumed; downstream
		// projections should not auto-retain it.
		Context.ForkActive = false;
	}

	private void ValidateFuseFollowsFork(double[]? weights)
	{
		// Fuse requires a Fork earlier in the pipeline (not necessarily immediately preceding).
		// ES|QL allows row-shape transformations like DROP / KEEP / RENAME / EVAL / WHERE between
		// them -- in particular DROP is recommended to remove dense_vector columns that FUSE rejects.
		// Aggregations (STATS) however collapse the fork-discriminator column and break FUSE.
		ForkCommand? matchingFork = null;
		for (var i = Context.Commands.Count - 1; i >= 0; i--)
		{
			var cmd = Context.Commands[i];
			if (cmd is ForkCommand fork)
			{
				matchingFork = fork;
				break;
			}

			if (cmd is StatsCommand)
				throw new InvalidOperationException("'Fuse' cannot follow a 'Stats' / aggregation command; aggregations break the FORK row layout.");
		}

		if (matchingFork is null)
			throw new InvalidOperationException("'Fuse' must follow a 'Fork' command earlier in the pipeline.");

		if (weights is not null && weights.Length != _lastForkBranchCount)
			throw new ArgumentException(
				$"Fuse weights count ({weights.Length}) must match the preceding Fork branch count ({_lastForkBranchCount}).",
				nameof(weights));

		// Per the ES|QL FUSE docs (Stack 9.4+ / Serverless), each FORK branch must contain a LIMIT
		// before FUSE. Older versions inject an implicit LIMIT 1000, but we validate eagerly so the
		// translator gives a clear error rather than relying on the server response.
		for (var b = 0; b < matchingFork.Branches.Count; b++)
		{
			var branch = matchingFork.Branches[b];
			var hasLimit = false;
			foreach (var fragment in branch)
			{
				if (fragment.StartsWith("LIMIT ", StringComparison.Ordinal) || fragment.Equals("LIMIT", StringComparison.Ordinal))
				{
					hasLimit = true;
					break;
				}
			}

			if (!hasLimit)
				throw new InvalidOperationException(
					$"Fork branch {b + 1} must include a 'Take(...)' (LIMIT) before 'Fuse'. " +
					"ES|QL requires a LIMIT inside each FORK branch when followed by FUSE.");
		}
	}

	private string ResolveSingleColumnFromLambda(LambdaExpression lambda, string commandName, string parameterName)
	{
		var body = lambda.Body.UnwrapConvertExpressions();

		// EsqlMetadata.X marker access -> emit underscore-prefixed identifier.
		if (body is MemberExpression { Expression: null, Member: { } metaMember }
			&& metaMember.DeclaringType == typeof(EsqlMetadata))
			return Context.ResolveMetadataMemberOrThrow(metaMember.Name);

		// Parameter-rooted member access (e.g. x => x.Score).
		if (body is MemberExpression member && ExpressionTranslationHelpers.IsRootedInParameter(member))
			return body.ResolveFieldName(Context.Metadata);

		throw new NotSupportedException(
			$"'{commandName}({parameterName}:)' must reference a single column " +
			$"(a parameter-rooted property or '{nameof(EsqlMetadata)}.X' marker), got '{body.NodeType}'.");
	}

	private List<string> ResolveKeyColumnsFromLambda(LambdaExpression lambda)
	{
		var body = lambda.Body.UnwrapConvertExpressions();

		// Composite key via anonymous type: x => new { x.Id, x.Index } or new { Id = EsqlMetadata.Id, Index = EsqlMetadata.Index }
		if (body is NewExpression newExpression)
		{
			if (newExpression.Members is null)
				throw new NotSupportedException(
					"Composite 'Fuse(key:)' must use an anonymous type, e.g. 'x => new { x.Id, x.Index }'.");

			var result = new List<string>(newExpression.Arguments.Count);
			for (var i = 0; i < newExpression.Arguments.Count; i++)
			{
				var arg = newExpression.Arguments[i].UnwrapConvertExpressions();

				if (arg is MemberExpression { Expression: null, Member: { } metaMember }
					&& metaMember.DeclaringType == typeof(EsqlMetadata))
				{
					result.Add(Context.ResolveMetadataMemberOrThrow(metaMember.Name));
					continue;
				}

				if (arg is MemberExpression keyMember && ExpressionTranslationHelpers.IsRootedInParameter(keyMember))
				{
					result.Add(arg.ResolveFieldName(Context.Metadata));
					continue;
				}

				throw new NotSupportedException(
					$"Each 'Fuse(key:)' member must be a parameter-rooted property or '{nameof(EsqlMetadata)}.X' marker, got '{arg.NodeType}'.");
			}
			return result;
		}

		// Single key column.
		return [ResolveSingleColumnFromLambda(lambda, nameof(EsqlQueryableExtensions.Fuse), "key")];
	}

	private static IReadOnlyList<string> NormalizeRawFragments(string rawEsql)
	{
		if (string.IsNullOrWhiteSpace(rawEsql))
			throw new NotSupportedException("RawEsql requires at least one non-empty fragment.");

		var normalized = rawEsql.Replace("\r\n", "\n")
			.Replace('\r', '\n');

		var fragments = normalized
			.Split('\n')
			.Select(NormalizeRawFragmentLine)
			.Where(fragment => !string.IsNullOrEmpty(fragment))
			.Select(fragment => fragment!)
			.ToList();

		if (fragments.Count == 0)
			throw new NotSupportedException("RawEsql requires at least one non-empty fragment.");

		return fragments;
	}

	private static string? NormalizeRawFragmentLine(string line)
	{
		var trimmed = line.Trim();
		if (trimmed.Length == 0)
			return null;

		if (trimmed[0] == '|')
			trimmed = trimmed[1..].TrimStart();

		return trimmed.Length == 0 ? null : trimmed;
	}

	private static Type? ResolveQueryableElementType(Type returnType)
	{
		var queryableType = TypeHelper.FindGenericType(typeof(IQueryable<>), returnType);
		return queryableType?.GetGenericArguments()[0];
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

		JoinTranslator.EmitJoin(lookupIndex, onCondition, resultSelectorArg);
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

		JoinTranslator.EmitJoin(lookupIndex, onCondition, resultSelectorArg, whereNotNullField: innerField);
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

			// Wrap the rewritten lambda back into a UnaryExpression so JoinTranslator.EmitJoin can unwrap it
			var quotedLambda = Expression.Quote(rewrittenLambda);
			JoinTranslator.EmitJoin(lookupIndex, onCondition, quotedLambda);
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
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Expression tree construction for GroupJoin rewriting; types are statically known.")]
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

	private string ExtractLookupIndex(Expression innerExpression)
	{
		if (innerExpression is ConstantExpression { Value: string indexName })
			return indexName;

		// Unwrap: if it's a ConstantExpression wrapping a queryable, use the queryable's expression
		if (innerExpression is ConstantExpression { Value: IQueryable innerQueryable })
			innerExpression = innerQueryable.Expression;

		var innerVisitor = new EsqlExpressionVisitor(Provider, inlineParameters);
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
				fieldNames.Add(ResolveSelectorFieldName(selectorLambda.Body));
		}
		return fieldNames;
	}

	private string ResolveSelectorFieldName(Expression expression)
	{
		expression = expression.UnwrapConvertExpressions();

		var fieldName = expression.ResolveFieldName(Context.Metadata);
		if (expression is not MemberExpression member)
			return fieldName;

		if (!ExpressionTranslationHelpers.IsObjectSelectionType(member.Type))
			return fieldName;

		return $"{fieldName}.*";
	}

	private string ExtractFieldName(Expression expression) =>
		expression.ResolveFieldName(Context.Metadata);
}
