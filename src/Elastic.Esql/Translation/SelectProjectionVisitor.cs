// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Core;
using Elastic.Esql.Formatting;
using Elastic.Esql.Functions;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ Select projections to ES|QL KEEP/EVAL commands.
/// </summary>
public class SelectProjectionVisitor(EsqlQueryContext context) : ExpressionVisitor
{
	private readonly EsqlQueryContext _context = context ?? throw new ArgumentNullException(nameof(context));
	private readonly List<string> _keepFields = [];
	private readonly List<string> _evalExpressions = [];

	/// <summary>
	/// Result of projection translation.
	/// </summary>
	public class ProjectionResult
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
		// Anonymous type or DTO projection: new { A = x.A, B = x.B + 1 }
		if (node.Members != null)
		{
			for (var i = 0; i < node.Arguments.Count; i++)
			{
				var arg = node.Arguments[i];
				var memberName = node.Members[i].Name;

				ProcessProjectionMember(memberName, arg);
			}
		}

		return node;
	}

	protected override Expression VisitMemberInit(MemberInitExpression node)
	{
		// Object initializer: new LogEntry { Timestamp = x.Timestamp, Level = x.Level }
		foreach (var binding in node.Bindings)
		{
			if (binding is MemberAssignment assignment)
			{
				var memberName = assignment.Member.Name;
				ProcessProjectionMember(memberName, assignment.Expression);
			}
		}

		return node;
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		// Simple member access: x => x.Field (identity projection)
		var fieldName = _context.MetadataResolver.Resolve(node.Member);
		_keepFields.Add(fieldName);

		return node;
	}

	private void ProcessProjectionMember(string resultName, Expression sourceExpression)
	{
		if (sourceExpression is MemberExpression memberExpr)
		{
			var declaringType = memberExpr.Member.DeclaringType;
			var resultField = ToCamelCase(resultName);

			// Check if this is a DateTime/DateTimeOffset property access (like l.Timestamp.Year)
			if (declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset))
			{
				var expr = TranslateMemberExpression(memberExpr);
				_evalExpressions.Add($"{resultField} = {expr}");
			}
			else
			{
				// Simple field access
				var sourceField = _context.MetadataResolver.Resolve(memberExpr.Member);

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
			var resultField = ToCamelCase(resultName);
			var expr = TranslateExpression(binary);
			_evalExpressions.Add($"{resultField} = {expr}");
		}
		else if (sourceExpression is MethodCallExpression methodCall)
		{
			// Method call: x.Field.ToUpper()
			var resultField = ToCamelCase(resultName);
			var expr = TranslateMethodCall(methodCall);
			_evalExpressions.Add($"{resultField} = {expr}");
		}
		else if (sourceExpression is ConditionalExpression conditional)
		{
			// Ternary: x.Field > 0 ? x.Field : 0
			var resultField = ToCamelCase(resultName);
			var expr = TranslateConditional(conditional);
			_evalExpressions.Add($"{resultField} = {expr}");
		}
		else if (sourceExpression is ConstantExpression constant)
		{
			// Constant value
			var resultField = ToCamelCase(resultName);
			var value = EsqlFormatting.FormatValue(constant.Value);
			_evalExpressions.Add($"{resultField} = {value}");
		}
	}

	private string TranslateExpression(Expression expression) =>
		expression switch
		{
			BinaryExpression binary => TranslateBinary(binary),
			MemberExpression member => TranslateMemberExpression(member),
			ConstantExpression constant => EsqlFormatting.FormatValue(constant.Value),
			UnaryExpression { NodeType: ExpressionType.Convert, Operand: var operand } => TranslateExpression(operand),
			MethodCallExpression methodCall => TranslateMethodCall(methodCall),
			_ => throw new NotSupportedException($"Expression type {expression.GetType().Name} is not supported in projections.")
		};

	private string TranslateMemberExpression(MemberExpression member)
	{
		var declaringType = member.Member.DeclaringType;
		var memberName = member.Member.Name;

		// Handle DateTime/DateTimeOffset static properties
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

		// Regular field access
		return _context.MetadataResolver.Resolve(member.Member);
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
			return TranslateEsqlFunction(methodCall);

		if (declaringType == typeof(string))
		{
			var target = TranslateExpression(methodCall.Object!);

			return methodName switch
			{
				"ToLower" or "ToLowerInvariant" => $"TO_LOWER({target})",
				"ToUpper" or "ToUpperInvariant" => $"TO_UPPER({target})",
				"Trim" => $"TRIM({target})",
				"Substring" when methodCall.Arguments.Count == 1 =>
					$"SUBSTRING({target}, {TranslateExpression(methodCall.Arguments[0])})",
				"Substring" when methodCall.Arguments.Count == 2 =>
					$"SUBSTRING({target}, {TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",
				// get_Chars is the indexer method: s[i] → SUBSTRING(s, i+1, 1)
				// ES|QL SUBSTRING uses 1-based indexing, C# uses 0-based
				"get_Chars" => TranslateStringIndexer(target, methodCall.Arguments[0]),
				_ => throw new NotSupportedException($"String method {methodName} is not supported in projections.")
			};
		}

		if (declaringType == typeof(Math))
		{
			return methodName switch
			{
				"Abs" => $"ABS({TranslateExpression(methodCall.Arguments[0])})",
				"Ceiling" => $"CEIL({TranslateExpression(methodCall.Arguments[0])})",
				"Floor" => $"FLOOR({TranslateExpression(methodCall.Arguments[0])})",
				"Round" when methodCall.Arguments.Count == 1 => $"ROUND({TranslateExpression(methodCall.Arguments[0])})",
				"Round" when methodCall.Arguments.Count == 2 => $"ROUND({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",
				"Max" => $"GREATEST({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",
				"Min" => $"LEAST({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",
				"Pow" => $"POW({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",
				"Sqrt" => $"SQRT({TranslateExpression(methodCall.Arguments[0])})",
				"Log" when methodCall.Arguments.Count == 1 => $"LOG({TranslateExpression(methodCall.Arguments[0])})",
				"Log10" => $"LOG10({TranslateExpression(methodCall.Arguments[0])})",
				_ => throw new NotSupportedException($"Math method {methodName} is not supported in projections.")
			};
		}

		throw new NotSupportedException($"Method {declaringType?.Name}.{methodName} is not supported in projections.");
	}

	private string TranslateEsqlFunction(MethodCallExpression methodCall)
	{
		var methodName = methodCall.Method.Name;

		return methodName switch
		{
			// Date/Time Functions
			"Now" => "NOW()",
			"DateTrunc" => $"DATE_TRUNC({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",
			"DateFormat" => $"DATE_FORMAT({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",

			// String Functions
			"Length" => $"LENGTH({TranslateExpression(methodCall.Arguments[0])})",
			"Substring" when methodCall.Arguments.Count == 2 =>
				$"SUBSTRING({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",
			"Substring" when methodCall.Arguments.Count == 3 =>
				$"SUBSTRING({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])}, {TranslateExpression(methodCall.Arguments[2])})",
			"Trim" => $"TRIM({TranslateExpression(methodCall.Arguments[0])})",
			"ToLower" => $"TO_LOWER({TranslateExpression(methodCall.Arguments[0])})",
			"ToUpper" => $"TO_UPPER({TranslateExpression(methodCall.Arguments[0])})",
			"Concat" => TranslateConcat(methodCall),

			// Null Handling
			"Coalesce" => TranslateCoalesce(methodCall),
			"IsNull" => $"{TranslateExpression(methodCall.Arguments[0])} IS NULL",
			"IsNotNull" => $"{TranslateExpression(methodCall.Arguments[0])} IS NOT NULL",

			// Math Functions
			"Abs" => $"ABS({TranslateExpression(methodCall.Arguments[0])})",
			"Ceil" => $"CEIL({TranslateExpression(methodCall.Arguments[0])})",
			"Floor" => $"FLOOR({TranslateExpression(methodCall.Arguments[0])})",
			"Round" when methodCall.Arguments.Count == 1 =>
				$"ROUND({TranslateExpression(methodCall.Arguments[0])})",
			"Round" when methodCall.Arguments.Count == 2 =>
				$"ROUND({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",

			// Pattern Matching
			"Match" => $"MATCH({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",
			"Like" => $"{TranslateExpression(methodCall.Arguments[0])} LIKE {TranslateExpression(methodCall.Arguments[1])}",
			"Rlike" => $"{TranslateExpression(methodCall.Arguments[0])} RLIKE {TranslateExpression(methodCall.Arguments[1])}",

			// IP Functions
			"CidrMatch" => $"CIDR_MATCH({TranslateExpression(methodCall.Arguments[0])}, {TranslateExpression(methodCall.Arguments[1])})",

			_ => throw new NotSupportedException($"ES|QL function {methodName} is not supported in projections.")
		};
	}

	private string TranslateConcat(MethodCallExpression methodCall)
	{
		// Concat takes params string[] - could be passed as array or as individual arguments
		var args = new List<string>();

		foreach (var arg in methodCall.Arguments)
		{
			if (arg is NewArrayExpression newArray)
			{
				// params array: Concat(new[] { a, b, c })
				foreach (var elem in newArray.Expressions)
					args.Add(TranslateExpression(elem));
			}
			else
				args.Add(TranslateExpression(arg));
		}

		return $"CONCAT({string.Join(", ", args)})";
	}

	private string TranslateCoalesce(MethodCallExpression methodCall)
	{
		// Coalesce takes params T[] - could be passed as array or as individual arguments
		var args = new List<string>();

		foreach (var arg in methodCall.Arguments)
		{
			if (arg is NewArrayExpression newArray)
			{
				// params array: Coalesce(new[] { a, b, c })
				foreach (var elem in newArray.Expressions)
					args.Add(TranslateExpression(elem));
			}
			else
				args.Add(TranslateExpression(arg));
		}

		return $"COALESCE({string.Join(", ", args)})";
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
		var test = TranslateExpression(conditional.Test);
		var ifTrue = TranslateExpression(conditional.IfTrue);
		var ifFalse = TranslateExpression(conditional.IfFalse);

		return $"CASE WHEN {test} THEN {ifTrue} ELSE {ifFalse} END";
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

	private static string ToCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		if (name.Length == 1)
			return name.ToLowerInvariant();

		return char.ToLowerInvariant(name[0]) + name.Substring(1);
	}
}
