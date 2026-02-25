// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Elastic.Esql.Core;
using Elastic.Esql.Formatting;
using Elastic.Esql.Functions;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ Select projections to ES|QL KEEP/EVAL commands.
/// </summary>
internal sealed class SelectProjectionVisitor(EsqlTranslationContext context) : ExpressionVisitor
{
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));
	private readonly List<string> _keepFields = [];
	private readonly List<string> _evalExpressions = [];

	/// <summary>
	/// Result of projection translation.
	/// </summary>
	public sealed class ProjectionResult
	{
		public IReadOnlyList<string> KeepFields { get; init; } = [];
		public IReadOnlyList<string> EvalExpressions { get; init; } = [];
	}

	/// <summary>
	/// Translates a Select lambda to projection commands.
	/// </summary>
	public ProjectionResult Translate(LambdaExpression lambda)
	{
		_keepFields.Clear();
		_evalExpressions.Clear();

		_ = Visit(lambda.Body);

		return new ProjectionResult
		{
			KeepFields = _keepFields.ToList(),
			EvalExpressions = _evalExpressions.ToList()
		};
	}

	protected override Expression VisitNew(NewExpression node)
	{
		if (node.Members == null)
			return node;

		var isAnonymous = node.Type.IsDefined(typeof(CompilerGeneratedAttribute), false);

		for (var i = 0; i < node.Arguments.Count; i++)
		{
			var arg = node.Arguments[i];
			var member = node.Members[i];

			var resultField = isAnonymous
				? _context.FieldMetadataResolver.GetAnonymousFieldName(member.Name)
				: _context.FieldMetadataResolver.GetFieldName(member.DeclaringType!, member);

			ProcessProjectionMember(resultField, arg);
		}

		return node;
	}

	protected override Expression VisitMemberInit(MemberInitExpression node)
	{
		foreach (var binding in node.Bindings)
		{
			if (binding is MemberAssignment assignment)
			{
				var resultField = _context.FieldMetadataResolver.GetFieldName(assignment.Member.DeclaringType!, assignment.Member);
				ProcessProjectionMember(resultField, assignment.Expression);
			}
		}

		return node;
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		// Simple member access: x => x.Field (identity projection)
		var fieldName = _context.FieldMetadataResolver.GetFieldName(node.Member.DeclaringType!, node.Member);
		_keepFields.Add(fieldName);

		return node;
	}

	private void ProcessProjectionMember(string resultField, Expression sourceExpression)
	{
		// Unwrap nullable casts: (int?)expr → expr (ES|QL fields are inherently nullable)
		if (sourceExpression is UnaryExpression { NodeType: ExpressionType.Convert } unary && IsNullableCast(unary))
		{
			ProcessProjectionMember(resultField, unary.Operand);
			return;
		}

		if (sourceExpression is MemberExpression memberExpr)
		{
			var declaringType = memberExpr.Member.DeclaringType;

			// Check if this is a DateTime/DateTimeOffset property or string.Length access
			if (declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset)
				|| (declaringType == typeof(string) && memberExpr.Member.Name == "Length"))
			{
				var expr = TranslateMemberExpression(memberExpr);
				_evalExpressions.Add($"{resultField} = {expr}");
			}
			else
			{
				// Simple field access
				var sourceField = _context.FieldMetadataResolver.GetFieldName(memberExpr.Member.DeclaringType!, memberExpr.Member);

				if (sourceField == resultField)
				{
					// Just keep the field as-is
					_keepFields.Add(sourceField);
				}
				else
				{
					// Rename via EVAL
					_evalExpressions.Add($"{resultField} = {sourceField}");
				}
			}
		}
		else if (sourceExpression is BinaryExpression binary)
		{
			// Computed field: x.A + x.B
			var expr = TranslateExpression(binary);
			_evalExpressions.Add($"{resultField} = {expr}");
		}
		else if (sourceExpression is MethodCallExpression methodCall)
		{
			// Method call: x.Field.ToUpper()
			var expr = TranslateMethodCall(methodCall);
			_evalExpressions.Add($"{resultField} = {expr}");
		}
		else if (sourceExpression is ConditionalExpression conditional)
		{
			if (TryUnwrapNullGuard(conditional, out var nonNullBranch) && IsSimpleFieldAccess(nonNullBranch))
			{
				// Simple null-guard: inner == null ? null : inner.Field → just the field
				ProcessProjectionMember(resultField, nonNullBranch);
			}
			else
			{
				// Complex null-guard → CASE WHEN, or regular ternary
				var expr = TranslateConditional(conditional);
				_evalExpressions.Add($"{resultField} = {expr}");
			}
		}
		else if (sourceExpression is ConstantExpression constant)
		{
			var value = EsqlFormatting.FormatValue(constant.Value);
			_evalExpressions.Add($"{resultField} = {value}");
		}
		else
			throw new NotSupportedException($"Expression type {sourceExpression.GetType().Name} ({sourceExpression.NodeType}) is not supported.");
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

		// One side must be a parameter, the other must be null
		if (!(left is ParameterExpression && IsNullConstant(right))
			&& !(right is ParameterExpression && IsNullConstant(left)))
			return false;

		if (test.NodeType == ExpressionType.Equal)
		{
			// param == null ? <null-branch> : <non-null-branch>
			if (!IsNullConstant(StripNullableConvert(conditional.IfTrue)))
				return false;

			nonNullBranch = StripNullableConvert(conditional.IfFalse);
		}
		else
		{
			// param != null ? <non-null-branch> : <null-branch>
			if (!IsNullConstant(StripNullableConvert(conditional.IfFalse)))
				return false;

			nonNullBranch = StripNullableConvert(conditional.IfTrue);
		}

		return true;
	}

	private static bool IsSimpleFieldAccess(Expression expression) =>
		expression is MemberExpression { Expression: ParameterExpression, Member.DeclaringType: not null } member
		&& member.Member.DeclaringType != typeof(DateTime)
		&& member.Member.DeclaringType != typeof(DateTimeOffset)
		&& !(member.Member.DeclaringType == typeof(string) && member.Member.Name == "Length");

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
			ConstantExpression constant => EsqlFormatting.FormatValue(constant.Value),
			UnaryExpression { NodeType: ExpressionType.Convert, Operand: var operand } => TranslateExpression(operand),
			MethodCallExpression methodCall => TranslateMethodCall(methodCall),
			ConditionalExpression conditional => TranslateConditional(conditional),
			_ => throw new NotSupportedException($"Expression type {expression.GetType().Name} is not supported in projections.")
		};

	private string TranslateMemberExpression(MemberExpression member)
	{
		var declaringType = member.Member.DeclaringType;
		var memberName = member.Member.Name;

		// Handle static member access
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

			// Math constants: Math.E, Math.PI, Math.Tau
			if (declaringType == typeof(Math))
			{
				var mathConst = EsqlFunctionTranslator.TryTranslateMathConstant(memberName);
				if (mathConst != null)
					return mathConst;
			}
		}

		// Handle DateTime/DateTimeOffset instance properties (Year, Month, Day, etc.)
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

		// Handle string.Length property → LENGTH(field)
		if (declaringType == typeof(string) && memberName == "Length")
		{
			var strExpr = TranslateExpression(member.Expression!);
			return $"LENGTH({strExpr})";
		}

		// Regular field access
		return _context.FieldMetadataResolver.GetFieldName(member.Member.DeclaringType!, member.Member);
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

		// Check for EsqlFunctions marker methods
		if (declaringType == typeof(EsqlFunctions))
		{
			var result = EsqlFunctionTranslator.TryTranslate(methodName, TranslateExpression, methodCall.Arguments);
			return result ?? throw new NotSupportedException($"ES|QL function {methodName} is not supported in projections.");
		}

		if (declaringType == typeof(string))
		{
			var target = TranslateExpression(methodCall.Object!);

			// Try shared translator first
			var result = EsqlFunctionTranslator.TryTranslateString(methodName, TranslateExpression, target, methodCall.Arguments);
			if (result != null)
				return result;

			// Fall back to string-specific methods not in the shared translator
			return methodName switch
			{
				// get_Chars is the indexer method: s[i] → SUBSTRING(s, i+1, 1)
				// ES|QL SUBSTRING uses 1-based indexing, C# uses 0-based
				"get_Chars" => TranslateStringIndexer(target, methodCall.Arguments[0]),
				_ => throw new NotSupportedException($"String method {methodName} is not supported in projections.")
			};
		}

		if (declaringType == typeof(Math))
		{
			var result = EsqlFunctionTranslator.TryTranslateMath(methodName, TranslateExpression, methodCall.Arguments);
			return result ?? throw new NotSupportedException($"Math method {methodName} is not supported in projections.");
		}

		throw new NotSupportedException($"Method {declaringType?.Name}.{methodName} is not supported in projections.");
	}

	private string TranslateStringIndexer(string target, Expression indexExpression)
	{
		// ES|QL SUBSTRING uses 1-based indexing, C# uses 0-based
		// s[i] → SUBSTRING(s, i+1, 1)
		if (indexExpression is ConstantExpression constant && constant.Value is int index)
			return $"SUBSTRING({target}, {index + 1}, 1)";

		// For non-constant index, we need to add 1 at runtime
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
		var visitor = new FieldAccessCollector(_context);
		_ = visitor.Visit(expression);
		return visitor.Fields;
	}

	/// <summary>
	/// Walks an expression tree collecting resolved field names from member accesses.
	/// </summary>
	private sealed class FieldAccessCollector(EsqlTranslationContext context) : ExpressionVisitor
	{
		public List<string> Fields { get; } = [];

		protected override Expression VisitMember(MemberExpression node)
		{
			if (node.Expression is ParameterExpression && node.Member.DeclaringType != null)
			{
				var fieldName = context.FieldMetadataResolver.GetFieldName(node.Member.DeclaringType, node.Member);
				if (!Fields.Contains(fieldName))
					Fields.Add(fieldName);
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
