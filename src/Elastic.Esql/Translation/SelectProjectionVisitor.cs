// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Elastic.Esql.Core;
using Elastic.Esql.Functions;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ Select projections to ES|QL RENAME/EVAL/KEEP commands using a two-pass design.
/// Pass 1 classifies each projection member into an intermediate representation.
/// Pass 2 translates eval expressions to strings with rename-awareness.
/// </summary>
internal sealed class SelectProjectionVisitor(EsqlTranslationContext context) : ExpressionVisitor
{
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));
	private readonly List<ProjectionEntry> _projections = [];
	private Dictionary<string, string> _activeRenames = [];

	private ParameterExpression? _outerParameter;
	private Dictionary<string, string>? _outerFieldRemappings;

	private enum ProjectionKind { Keep, Rename, Eval }

	private sealed record ProjectionEntry(ProjectionKind Kind, string ResultField, string? SourceField, Expression? SourceExpression);

	/// <summary>
	/// Result of projection translation.
	/// </summary>
	public sealed class ProjectionResult
	{
		public IReadOnlyList<string> KeepFields { get; init; } = [];
		public IReadOnlyList<(string Source, string Target)> RenameFields { get; init; } = [];
		public IReadOnlyList<string> EvalExpressions { get; init; } = [];
	}

	/// <summary>
	/// Translates a Select lambda to projection commands.
	/// </summary>
	public ProjectionResult Translate(LambdaExpression lambda) =>
		TranslateCore(lambda);

	/// <summary>
	/// Translates a join result selector lambda to projection commands, applying
	/// outer field remappings so that <c>outer.X</c> references resolve to the
	/// EVAL-preserved temp field instead of the post-join (overwritten) column.
	/// </summary>
	public ProjectionResult TranslateJoinProjection(
		LambdaExpression lambda,
		ParameterExpression outerParam,
		Dictionary<string, string> outerFieldRemappings
	)
	{
		_outerParameter = outerParam;
		_outerFieldRemappings = outerFieldRemappings;
		try
		{
			return TranslateCore(lambda);
		}
		finally
		{
			_outerParameter = null;
			_outerFieldRemappings = null;
		}
	}

	private ProjectionResult TranslateCore(LambdaExpression lambda)
	{
		_projections.Clear();
		_activeRenames = [];

		// Pass 1: classify all projection members
		_ = Visit(lambda.Body);

		// Build rename map so Pass 2 resolves renamed fields correctly
		_activeRenames = _projections
			.Where(p => p.Kind == ProjectionKind.Rename)
			.ToDictionary(p => p.SourceField!, p => p.ResultField);

		// Pass 2: translate eval expressions to strings (now rename-aware)
		var keepFields = new List<string>();
		var renameFields = new List<(string, string)>();
		var evalExpressions = new List<string>();

		foreach (var entry in _projections)
		{
			switch (entry.Kind)
			{
				case ProjectionKind.Keep:
					keepFields.Add(entry.SourceField!);
					break;
				case ProjectionKind.Rename:
					renameFields.Add((entry.SourceField!, entry.ResultField));
					break;
				case ProjectionKind.Eval:
					var expr = TranslateExpression(entry.SourceExpression!);
					evalExpressions.Add($"{entry.ResultField} = {expr}");
					break;
			}
		}

		return new ProjectionResult
		{
			KeepFields = keepFields,
			RenameFields = renameFields,
			EvalExpressions = evalExpressions
		};
	}

	protected override Expression VisitNew(NewExpression node)
	{
		if (node.Members is not null)
		{
			var isAnonymous = node.Type.IsDefined(typeof(CompilerGeneratedAttribute), false);
			HashSet<string>? anonymousFieldNames = isAnonymous ? new(StringComparer.Ordinal) : null;

			for (var i = 0; i < node.Arguments.Count; i++)
			{
				var arg = node.Arguments[i];
				var member = node.Members[i];

				var resultField = _context.ResolveFieldName(member.DeclaringType!, member);
				_ = anonymousFieldNames?.Add(resultField);

				ClassifyProjectionMember(resultField, arg);
			}

			if (anonymousFieldNames is not null)
				_context.RegisterAnonymousTypeFields(node.Type, anonymousFieldNames);

			return node;
		}

		if (node.Constructor is null)
			return node;

		var parameters = node.Constructor.GetParameters();
		var propertyMap = _context.Metadata.GetConstructorPropertyMap(node.Type);

		for (var i = 0; i < node.Arguments.Count; i++)
		{
			var paramName = parameters[i].Name
				?? throw new NotSupportedException(
					$"Constructor parameter at index {i} on type '{node.Type.Name}' has no name.");

			if (!propertyMap.TryGetValue(paramName, out var jsonProp))
				throw new NotSupportedException(
					$"Constructor parameter '{paramName}' on type '{node.Type.Name}' " +
					"does not match any serializable property. " +
					"Ensure each parameter name matches a property name (case-insensitive).");

			ClassifyProjectionMember(jsonProp.Name, node.Arguments[i]);
		}

		return node;
	}

	protected override Expression VisitMemberInit(MemberInitExpression node)
	{
		foreach (var binding in node.Bindings)
		{
			if (binding is MemberAssignment assignment)
			{
				var resultField = _context.ResolveFieldName(assignment.Member.DeclaringType!, assignment.Member);
				ClassifyProjectionMember(resultField, assignment.Expression);
			}
		}

		return node;
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		var fieldName = node.ResolveFieldName(_context.Metadata);
		if (ExpressionTranslationHelpers.IsObjectSelectionType(node.Type))
			fieldName = $"{fieldName}.*";

		_projections.Add(new ProjectionEntry(ProjectionKind.Keep, fieldName, fieldName, null));

		return node;
	}

	private void ClassifyProjectionMember(string resultField, Expression sourceExpression)
	{
		if (sourceExpression is UnaryExpression { NodeType: ExpressionType.Convert } unary && IsNullableCast(unary))
		{
			ClassifyProjectionMember(resultField, unary.Operand);
			return;
		}

		if (TryClassifyNestedProjection(resultField, sourceExpression))
			return;

		if (sourceExpression is MemberExpression memberExpr)
		{
			var declaringType = memberExpr.Member.DeclaringType;

			if (declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset)
				|| (declaringType == typeof(string) && memberExpr.Member.Name == "Length"))
			{
				_projections.Add(new ProjectionEntry(ProjectionKind.Eval, resultField, null, memberExpr));
			}
			else
			{
				var sourceField = memberExpr.ResolveFieldName(_context.Metadata);
				sourceField = ApplyOuterRemapping(memberExpr, sourceField);

				if (ExpressionTranslationHelpers.IsObjectSelectionType(memberExpr.Type))
				{
					if (sourceField != resultField)
						throw new NotSupportedException(
							$"Aliasing object selections is not supported for '{sourceField}'. " +
							$"Select specific sub-fields or keep '{sourceField}.*'.");

					var wildcardField = $"{sourceField}.*";
					_projections.Add(new ProjectionEntry(ProjectionKind.Keep, wildcardField, wildcardField, null));
					return;
				}

				if (sourceField == resultField)
					_projections.Add(new ProjectionEntry(ProjectionKind.Keep, sourceField, sourceField, null));
				else
					_projections.Add(new ProjectionEntry(ProjectionKind.Rename, resultField, sourceField, null));
			}
		}
		else if (sourceExpression is ConditionalExpression conditional
			&& TryUnwrapNullGuard(conditional, out var nonNullBranch)
			&& IsSimpleFieldAccess(nonNullBranch))
		{
			ClassifyProjectionMember(resultField, nonNullBranch);
		}
		else if (sourceExpression is BinaryExpression or MethodCallExpression or ConditionalExpression or ConstantExpression)
		{
			_projections.Add(new ProjectionEntry(ProjectionKind.Eval, resultField, null, sourceExpression));
		}
		else
			throw new NotSupportedException($"Expression type {sourceExpression.GetType().Name} ({sourceExpression.NodeType}) is not supported.");
	}

	private bool TryClassifyNestedProjection(string resultField, Expression sourceExpression)
	{
		if (sourceExpression is NewExpression { Members: not null } newExpression)
		{
			for (var i = 0; i < newExpression.Arguments.Count; i++)
			{
				var member = newExpression.Members[i];
				var nestedResultField = BuildNestedResultField(resultField, member);
				ClassifyProjectionMember(nestedResultField, newExpression.Arguments[i]);
			}

			return true;
		}

		if (sourceExpression is MemberInitExpression memberInitExpression)
		{
			foreach (var binding in memberInitExpression.Bindings)
			{
				if (binding is not MemberAssignment assignment)
					continue;

				var nestedResultField = BuildNestedResultField(resultField, assignment.Member);
				ClassifyProjectionMember(nestedResultField, assignment.Expression);
			}

			return true;
		}

		return false;
	}

	private string BuildNestedResultField(string resultFieldPrefix, MemberInfo member)
	{
		var childField = _context.ResolveFieldName(member.DeclaringType!, member);
		return $"{resultFieldPrefix}.{childField}";
	}

	/// <summary>
	/// Detects null-guard ternary patterns like <c>param == null ? null : param.Field</c>
	/// or <c>param != null ? param.Field : null</c> where one side of the test is a
	/// <see cref="ParameterExpression"/> compared to null, and one branch is null/default.
	/// </summary>
	private static bool TryUnwrapNullGuard(ConditionalExpression conditional, out Expression nonNullBranch)
	{
		nonNullBranch = null!;

		if (conditional.Test is not BinaryExpression
			{
				NodeType: ExpressionType.Equal or ExpressionType.NotEqual
			} test)
			return false;

		var left = StripNullableConvert(test.Left);
		var right = StripNullableConvert(test.Right);

		if (!(left is ParameterExpression && IsNullConstant(right))
			&& !(right is ParameterExpression && IsNullConstant(left)))
			return false;

		if (test.NodeType == ExpressionType.Equal)
		{
			if (!IsNullConstant(StripNullableConvert(conditional.IfTrue)))
				return false;

			nonNullBranch = StripNullableConvert(conditional.IfFalse);
		}
		else
		{
			if (!IsNullConstant(StripNullableConvert(conditional.IfFalse)))
				return false;

			nonNullBranch = StripNullableConvert(conditional.IfTrue);
		}

		return true;
	}

	private static bool IsSimpleFieldAccess(Expression expression)
	{
		if (expression is not MemberExpression { Member.DeclaringType: not null } member)
			return false;

		if (member.Member.DeclaringType == typeof(DateTime)
			|| member.Member.DeclaringType == typeof(DateTimeOffset)
			|| (member.Member.DeclaringType == typeof(string) && member.Member.Name == "Length"))
			return false;

		return ExpressionTranslationHelpers.IsRootedInParameter(member);
	}

	/// <summary>
	/// If the member access is on the outer parameter and the field name is in the
	/// remapping dictionary, returns the temp field name; otherwise returns the original.
	/// </summary>
	private string ApplyOuterRemapping(MemberExpression memberExpr, string fieldName)
	{
		if (_outerFieldRemappings is null || _outerParameter is null)
			return fieldName;

		if (ExpressionTranslationHelpers.IsRootedInParameter(memberExpr, _outerParameter)
			&& TryResolveOuterRemappedField(fieldName, _outerFieldRemappings, out var remapped))
			return remapped;

		return fieldName;
	}

	private static bool TryResolveOuterRemappedField(
		string fieldName,
		Dictionary<string, string> outerFieldRemappings,
		out string remappedField)
	{
		if (outerFieldRemappings.TryGetValue(fieldName, out var exact))
		{
			remappedField = exact;
			return true;
		}

		string? bestPrefix = null;
		string? bestRemappedPrefix = null;

		foreach (var remapping in outerFieldRemappings)
		{
			var sourcePrefix = remapping.Key;
			var targetPrefix = remapping.Value;
			if (!fieldName.StartsWith(sourcePrefix, StringComparison.Ordinal))
				continue;

			if (fieldName.Length != sourcePrefix.Length && fieldName[sourcePrefix.Length] != '.')
				continue;

			if (bestPrefix is not null && bestPrefix.Length >= sourcePrefix.Length)
				continue;

			bestPrefix = sourcePrefix;
			bestRemappedPrefix = targetPrefix;
		}

		if (bestPrefix is null || bestRemappedPrefix is null)
		{
			remappedField = string.Empty;
			return false;
		}

		remappedField = fieldName.Length == bestPrefix.Length
			? bestRemappedPrefix
			: $"{bestRemappedPrefix}{fieldName[bestPrefix.Length..]}";

		return true;
	}

	private static bool IsNullableCast(UnaryExpression unary)
	{
		var targetType = unary.Type;
		return targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>);
	}

	private static Expression StripNullableConvert(Expression expression) =>
		expression is UnaryExpression { NodeType: ExpressionType.Convert } convert && IsNullableCast(convert)
			? convert.Operand
			: expression;

	private static bool IsNullConstant(Expression expression) =>
		expression is ConstantExpression { Value: null } or DefaultExpression;

	private string TranslateExpression(Expression expression) =>
		expression switch
		{
			BinaryExpression binary => TranslateBinary(binary),
			MemberExpression member => TranslateMemberExpression(member),
			ConstantExpression constant => _context.FormatValue(constant.Value),
			UnaryExpression { NodeType: ExpressionType.Convert, Operand: var operand } => TranslateExpression(operand),
			MethodCallExpression methodCall => TranslateMethodCall(methodCall),
			ConditionalExpression conditional => TranslateConditional(conditional),
			_ => throw new NotSupportedException($"Expression type {expression.GetType().Name} is not supported in projections.")
		};

	private string TranslateMemberExpression(MemberExpression member)
	{
		var declaringType = member.Member.DeclaringType;
		var memberName = member.Member.Name;

		if (member.Expression == null)
		{
			if (declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset))
			{
				return memberName switch
				{
					"Now" or "UtcNow" => "NOW()",
					"Today" => "DATE_TRUNC(\"day\", NOW())",
					_ => throw new NotSupportedException($"DateTime property {memberName} is not supported in projections.")
				};
			}

			if (declaringType == typeof(Math))
			{
				var mathConst = EsqlFunctionTranslator.TryTranslateMathConstant(memberName);
				if (mathConst != null)
					return mathConst;
			}
		}

		if (declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset))
		{
			var dateExpr = TranslateExpression(member.Expression!);
			return memberName switch
			{
				"Year" => $"DATE_EXTRACT(\"year\", {dateExpr})",
				"Month" => $"DATE_EXTRACT(\"month\", {dateExpr})",
				"Day" => $"DATE_EXTRACT(\"day_of_month\", {dateExpr})",
				"Hour" => $"DATE_EXTRACT(\"hour\", {dateExpr})",
				"Minute" => $"DATE_EXTRACT(\"minute\", {dateExpr})",
				"Second" => $"DATE_EXTRACT(\"second\", {dateExpr})",
				"DayOfWeek" => $"DATE_EXTRACT(\"day_of_week\", {dateExpr})",
				"DayOfYear" => $"DATE_EXTRACT(\"day_of_year\", {dateExpr})",
				_ => throw new NotSupportedException($"DateTime property {memberName} is not supported in projections.")
			};
		}

		if (declaringType == typeof(string) && memberName == "Length")
		{
			var strExpr = TranslateExpression(member.Expression!);
			return $"LENGTH({strExpr})";
		}

		var fieldName = member.ResolveFieldName(_context.Metadata);
		fieldName = ApplyOuterRemapping(member, fieldName);
		return _activeRenames.TryGetValue(fieldName, out var renamed) ? renamed : fieldName;
	}

	private string TranslateBinary(BinaryExpression binary)
	{
		var left = TranslateExpression(binary.Left);
		var right = TranslateExpression(binary.Right);
		var op = GetOperator(binary.NodeType);

		return $"({left} {op} {right})";
	}

	private string TranslateMethodCall(MethodCallExpression methodCall)
	{
		var methodName = methodCall.Method.Name;
		var declaringType = methodCall.Method.DeclaringType;
		var translated = EsqlFunctionTranslator.TryTranslateMethodCall(methodCall, TranslateExpression);
		if (translated != null)
			return translated;

		if (declaringType == typeof(string) && methodCall.Object is not null)
		{
			var target = TranslateExpression(methodCall.Object);
			return methodName switch
			{
				"get_Chars" => TranslateStringIndexer(target, methodCall.Arguments[0]),
				_ => throw new NotSupportedException($"String method {methodName} is not supported in projections.")
			};
		}

		throw new NotSupportedException($"Method {declaringType?.Name}.{methodName} is not supported in projections.");
	}

	private string TranslateStringIndexer(string target, Expression indexExpression)
	{
		if (indexExpression is ConstantExpression constant && constant.Value is int index)
			return $"SUBSTRING({target}, {index + 1}, 1)";

		var indexExpr = TranslateExpression(indexExpression);
		return $"SUBSTRING({target}, ({indexExpr}) + 1, 1)";
	}

	private string TranslateConditional(ConditionalExpression conditional)
	{
		if (TryUnwrapNullGuard(conditional, out var nonNullBranch))
		{
			var nullCheckFields = ExtractNullCheckFields(nonNullBranch);
			if (nullCheckFields.Count > 0)
			{
				var nullCheck = string.Join(" AND ", nullCheckFields.Select(f => $"{f} IS NOT NULL"));
				var expr = TranslateExpression(nonNullBranch);
				return $"CASE WHEN {nullCheck} THEN {expr} ELSE NULL END";
			}
		}

		var test = TranslateExpression(conditional.Test);
		var ifTrue = TranslateExpression(conditional.IfTrue);
		var ifFalse = TranslateExpression(conditional.IfFalse);

		return $"CASE WHEN {test} THEN {ifTrue} ELSE {ifFalse} END";
	}

	/// <summary>
	/// Extracts field names from the non-null branch to use for IS NOT NULL checks
	/// in a null-guard CASE WHEN expression.
	/// </summary>
	private List<string> ExtractNullCheckFields(Expression nonNullBranch) =>
		ExtractFieldAccesses(nonNullBranch);

	private List<string> ExtractFieldAccesses(Expression expression)
	{
		var visitor = new FieldAccessCollector(_context, _activeRenames, _outerParameter, _outerFieldRemappings);
		_ = visitor.Visit(expression);
		return visitor.Fields;
	}

	/// <summary>
	/// Walks an expression tree collecting resolved field names from member accesses.
	/// </summary>
	private sealed class FieldAccessCollector(
		EsqlTranslationContext context,
		Dictionary<string, string> activeRenames,
		ParameterExpression? outerParameter,
		Dictionary<string, string>? outerFieldRemappings
	) : ExpressionVisitor
	{
		private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

		public List<string> Fields { get; } = [];

		protected override Expression VisitMember(MemberExpression node)
		{
			if (node.Member.DeclaringType != null && ExpressionTranslationHelpers.IsRootedInParameter(node))
			{
				var fieldName = node.ResolveFieldName(context.Metadata);

				if (outerParameter is not null
					&& outerFieldRemappings is not null
					&& ExpressionTranslationHelpers.IsRootedInParameter(node, outerParameter)
					&& TryResolveOuterRemappedField(fieldName, outerFieldRemappings, out var remapped))
					fieldName = remapped;

				if (activeRenames.TryGetValue(fieldName, out var renamed))
					fieldName = renamed;

				if (_seen.Add(fieldName))
					Fields.Add(fieldName);

				// Avoid descending into parent member nodes to prevent collecting
				// intermediate prefixes for nested paths (e.g. "address" when collecting "address.city").
				return node;
			}

			return base.VisitMember(node);
		}
	}

	private static string GetOperator(ExpressionType nodeType) =>
		nodeType switch
		{
			ExpressionType.Add => "+",
			ExpressionType.Subtract => "-",
			ExpressionType.Multiply => "*",
			ExpressionType.Divide => "/",
			ExpressionType.Modulo => "%",
			ExpressionType.Equal => "==",
			ExpressionType.NotEqual => "!=",
			ExpressionType.LessThan => "<",
			ExpressionType.LessThanOrEqual => "<=",
			ExpressionType.GreaterThan => ">",
			ExpressionType.GreaterThanOrEqual => ">=",
			ExpressionType.AndAlso => "AND",
			ExpressionType.OrElse => "OR",
			_ => throw new NotSupportedException($"Operator {nodeType} is not supported in projections.")
		};
}
