// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Elastic.Esql.Core;
using Elastic.Esql.Functions;
using Elastic.Esql.TypeMapping;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ predicate expressions to ES|QL WHERE conditions.
/// </summary>
public class WhereClauseVisitor(EsqlQueryContext context) : ExpressionVisitor
{
	private readonly EsqlQueryContext _context = context ?? throw new ArgumentNullException(nameof(context));
	private readonly StringBuilder _builder = new();

	/// <summary>
	/// Translates a predicate expression to an ES|QL condition string.
	/// </summary>
	public string Translate(Expression expression)
	{
		_ = _builder.Clear();
		_ = Visit(expression);
		return _builder.ToString();
	}

	protected override Expression VisitBinary(BinaryExpression node)
	{
		// Only add parentheses for logical operators (AND/OR) to ensure proper grouping
		var isLogicalOperator = node.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse;

		if (isLogicalOperator)
			_ = _builder.Append('(');

		// Check for enum comparison that needs string formatting
		var enumContext = GetEnumComparisonContext(node);
		if (enumContext.HasValue)
		{
			_ = Visit(enumContext.Value.MemberSide);
			var op = GetOperator(node.NodeType);
			_ = _builder.Append(' ').Append(op).Append(' ');
			_ = _builder.Append(FormatEnumValue(enumContext.Value.EnumType, enumContext.Value.ConstantValue));
		}
		else
		{
			_ = Visit(node.Left);
			var op = GetOperator(node.NodeType);
			_ = _builder.Append(' ').Append(op).Append(' ');
			_ = Visit(node.Right);
		}

		if (isLogicalOperator)
			_ = _builder.Append(')');

		return node;
	}

	private (Expression MemberSide, Type EnumType, object? ConstantValue)? GetEnumComparisonContext(BinaryExpression node)
	{
		// Only handle equality/inequality comparisons
		if (node.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
			return null;

		// Try left=member, right=constant
		var leftEnumType = GetEnumTypeFromExpression(node.Left);
		if (leftEnumType != null)
		{
			var rightValue = TryGetConstantValue(node.Right);
			if (rightValue != null && ShouldFormatEnumAsString(leftEnumType))
				return (node.Left, leftEnumType, rightValue);
		}

		// Try right=member, left=constant
		var rightEnumType = GetEnumTypeFromExpression(node.Right);
		if (rightEnumType != null)
		{
			var leftValue = TryGetConstantValue(node.Left);
			if (leftValue != null && ShouldFormatEnumAsString(rightEnumType))
				return (node.Right, rightEnumType, leftValue);
		}

		return null;
	}

	private static Type? GetEnumTypeFromExpression(Expression expr)
	{
		// Unwrap Convert expressions
		while (expr is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
			expr = unary.Operand;

		if (expr is not MemberExpression member)
			return null;

		var memberType = member.Member switch
		{
			PropertyInfo prop => prop.PropertyType,
			FieldInfo field => field.FieldType,
			_ => null
		};

		if (memberType == null)
			return null;

		// Handle nullable enums
		var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;
		return underlying.IsEnum ? underlying : null;
	}

	private static bool ShouldFormatEnumAsString(Type enumType)
	{
		// Always format enums as strings since keyword fields expect strings.
		// The JsonStringEnumConverter attribute on the property ensures the data is stored as strings.
		_ = enumType; // Suppress unused parameter warning
		return true;
	}

	private static object? TryGetConstantValue(Expression expression)
	{
		// Unwrap Convert expressions
		while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
			expression = unary.Operand;

		return expression switch
		{
			ConstantExpression constant => constant.Value,
			MemberExpression member when member.Expression is ConstantExpression ce => GetMemberValue(member, ce.Value),
			_ => null
		};
	}

	private static string FormatEnumValue(Type enumType, object? value)
	{
		if (value == null)
			return "null";

		// Convert the value to the enum and get its name
		var enumValue = Enum.ToObject(enumType, value);
		var name = Enum.GetName(enumType, enumValue);
		return EsqlTypeMapper.FormatValue(name ?? value.ToString() ?? "");
	}

	protected override Expression VisitUnary(UnaryExpression node)
	{
		switch (node.NodeType)
		{
			case ExpressionType.Not:
				_ = _builder.Append("NOT ");
				_ = Visit(node.Operand);
				break;

			case ExpressionType.Convert:
			case ExpressionType.ConvertChecked:
				// Just visit the operand, ES|QL handles type coercion
				_ = Visit(node.Operand);
				break;

			default:
				throw new NotSupportedException($"Unary operator {node.NodeType} is not supported.");
		}

		return node;
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		// Check if this is accessing a captured variable (closure)
		if (node.Expression is ConstantExpression constantExpression)
		{
			var value = GetMemberValue(node, constantExpression.Value);
			_ = _builder.Append(EsqlTypeMapper.FormatValue(value));
			return node;
		}

		// Check if this is a nested member access on a captured variable
		if (node.Expression is MemberExpression innerMember &&
			innerMember.Expression is ConstantExpression innerConstant)
		{
			var innerValue = GetMemberValue(innerMember, innerConstant.Value);
			var value = GetMemberValue(node, innerValue);
			_ = _builder.Append(EsqlTypeMapper.FormatValue(value));
			return node;
		}

		// Check for static member access (like DateTime.UtcNow)
		if (node.Expression == null)
		{
			// Handle DateTime/DateTimeOffset static properties that should translate to NOW()
			var declaringType = node.Member.DeclaringType;
			var memberName = node.Member.Name;

			if (declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset))
			{
				switch (memberName)
				{
					case "Now":
					case "UtcNow":
						_ = _builder.Append("NOW()");
						return node;
					case "Today":
						_ = _builder.Append("DATE_TRUNC(\"day\", NOW())");
						return node;
				}
			}

			// For other static members, evaluate the value
			var value = GetStaticMemberValue(node);
			_ = _builder.Append(EsqlTypeMapper.FormatValue(value));
			return node;
		}

		// Check for DateTime/DateTimeOffset property access (Year, Month, Day, etc.)
		if (node.Member.DeclaringType == typeof(DateTime) || node.Member.DeclaringType == typeof(DateTimeOffset))
		{
			var dateExpr = TranslateDateTimeExpression(node.Expression!);
			var memberName = node.Member.Name;

			switch (memberName)
			{
				case "Year":
					_ = _builder.Append("DATE_EXTRACT(\"year\", ").Append(dateExpr).Append(')');
					return node;
				case "Month":
					_ = _builder.Append("DATE_EXTRACT(\"month\", ").Append(dateExpr).Append(')');
					return node;
				case "Day":
					_ = _builder.Append("DATE_EXTRACT(\"day_of_month\", ").Append(dateExpr).Append(')');
					return node;
				case "Hour":
					_ = _builder.Append("DATE_EXTRACT(\"hour\", ").Append(dateExpr).Append(')');
					return node;
				case "Minute":
					_ = _builder.Append("DATE_EXTRACT(\"minute\", ").Append(dateExpr).Append(')');
					return node;
				case "Second":
					_ = _builder.Append("DATE_EXTRACT(\"second\", ").Append(dateExpr).Append(')');
					return node;
				case "DayOfWeek":
					_ = _builder.Append("DATE_EXTRACT(\"day_of_week\", ").Append(dateExpr).Append(')');
					return node;
				case "DayOfYear":
					_ = _builder.Append("DATE_EXTRACT(\"day_of_year\", ").Append(dateExpr).Append(')');
					return node;
			}
		}

		// Regular field access
		var fieldName = _context.FieldNameResolver.Resolve(node.Member);
		_ = _builder.Append(fieldName);

		return node;
	}

	private string TranslateDateTimeExpression(Expression expression) =>
		// Recursively translate the inner expression
		expression switch
		{
			MemberExpression member when member.Expression == null =>
				// Static property like DateTime.UtcNow
				TranslateStaticDateTimeProperty(member),
			MemberExpression member =>
				// Field access like l.Timestamp
				_context.FieldNameResolver.Resolve(member.Member),
			MethodCallExpression methodCall when methodCall.Method.DeclaringType == typeof(EsqlFunctions) =>
				TranslateEsqlFunctionForDateTime(methodCall),
			_ => throw new NotSupportedException($"Expression type {expression.GetType().Name} is not supported for DateTime property access.")
		};

	private string TranslateStaticDateTimeProperty(MemberExpression member)
	{
		var memberName = member.Member.Name;
		return memberName switch
		{
			"Now" or "UtcNow" => "NOW()",
			"Today" => "DATE_TRUNC(\"day\", NOW())",
			_ => throw new NotSupportedException($"DateTime static property {memberName} is not supported.")
		};
	}

	private string TranslateEsqlFunctionForDateTime(MethodCallExpression methodCall)
	{
		var methodName = methodCall.Method.Name;
		return methodName switch
		{
			"Now" => "NOW()",
			"DateTrunc" => $"DATE_TRUNC({TranslateDateTimeExpression(methodCall.Arguments[0])}, {TranslateDateTimeExpression(methodCall.Arguments[1])})",
			_ => throw new NotSupportedException($"EsqlFunction {methodName} is not supported in DateTime context.")
		};
	}

	protected override Expression VisitConstant(ConstantExpression node)
	{
		_ = _builder.Append(EsqlTypeMapper.FormatValue(node.Value));
		return node;
	}

	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		var methodName = node.Method.Name;
		var declaringType = node.Method.DeclaringType;

		// Check for EsqlFunctions marker methods
		if (declaringType == typeof(EsqlFunctions))
			return VisitEsqlFunction(node);

		// String methods
		if (declaringType == typeof(string))
			return VisitStringMethod(node);

		// Enumerable methods (Contains for IN operator)
		if (declaringType == typeof(Enumerable) ||
			(declaringType?.IsGenericType == true &&
			 declaringType.GetGenericTypeDefinition() == typeof(List<>)))
		{
			if (methodName == "Contains")
				return VisitContains(node);
		}

		// List<T>.Contains
		if (node.Object != null && methodName == "Contains")
		{
			var objectType = node.Object.Type;
			if (objectType.IsGenericType &&
				(objectType.GetGenericTypeDefinition() == typeof(List<>) ||
				 objectType.GetGenericTypeDefinition() == typeof(ICollection<>) ||
				 objectType.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
				return VisitListContains(node);
		}

		// DateTime methods
		if (declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset))
			return VisitDateTimeMethod(node);

		// TimeSpan static methods
		if (declaringType == typeof(TimeSpan))
			return VisitTimeSpanMethod(node);

		throw new NotSupportedException($"Method {declaringType?.Name}.{methodName} is not supported.");
	}

	private Expression VisitTimeSpanMethod(MethodCallExpression node)
	{
		var methodName = node.Method.Name;
		var arg = GetConstantValue(node.Arguments[0]);

		// Convert the numeric value to appropriate ES|QL time interval
		return methodName switch
		{
			"FromDays" => AppendTimeInterval(arg, "days"),
			"FromHours" => AppendTimeInterval(arg, "hours"),
			"FromMinutes" => AppendTimeInterval(arg, "minutes"),
			"FromSeconds" => AppendTimeInterval(arg, "seconds"),
			"FromMilliseconds" => AppendTimeInterval(arg, "milliseconds"),
			_ => throw new NotSupportedException($"TimeSpan method {methodName} is not supported.")
		};
	}

	private Expression AppendTimeInterval(object? value, string unit)
	{
		// Format as ES|QL time interval (e.g., "1 hour", "30 minutes")
		_ = _builder.Append(CultureInfo.InvariantCulture, $"{value} {unit}");
		return Expression.Empty();
	}

	private Expression VisitEsqlFunction(MethodCallExpression node)
	{
		var methodName = node.Method.Name;

		switch (methodName)
		{
			case "Now":
				_ = _builder.Append("NOW()");
				break;

			case "Match":
				_ = _builder.Append("MATCH(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(", ");
				_ = Visit(node.Arguments[1]);
				_ = _builder.Append(')');
				break;

			case "Like":
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" LIKE ");
				_ = Visit(node.Arguments[1]);
				break;

			case "Rlike":
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" RLIKE ");
				_ = Visit(node.Arguments[1]);
				break;

			case "IsNull":
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" IS NULL");
				break;

			case "IsNotNull":
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" IS NOT NULL");
				break;

			case "DateTrunc":
				_ = _builder.Append("DATE_TRUNC(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(", ");
				_ = Visit(node.Arguments[1]);
				_ = _builder.Append(')');
				break;

			case "DateFormat":
				_ = _builder.Append("DATE_FORMAT(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(", ");
				_ = Visit(node.Arguments[1]);
				_ = _builder.Append(')');
				break;

			case "Length":
				_ = _builder.Append("LENGTH(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(')');
				break;

			case "Substring":
				_ = _builder.Append("SUBSTRING(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(", ");
				_ = Visit(node.Arguments[1]);
				if (node.Arguments.Count > 2)
				{
					_ = _builder.Append(", ");
					_ = Visit(node.Arguments[2]);
				}

				_ = _builder.Append(')');
				break;

			case "Trim":
				_ = _builder.Append("TRIM(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(')');
				break;

			case "ToLower":
				_ = _builder.Append("TO_LOWER(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(')');
				break;

			case "ToUpper":
				_ = _builder.Append("TO_UPPER(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(')');
				break;

			case "Concat":
				_ = _builder.Append("CONCAT(");
				for (var i = 0; i < node.Arguments.Count; i++)
				{
					if (i > 0)
						_ = _builder.Append(", ");
					_ = Visit(node.Arguments[i]);
				}

				_ = _builder.Append(')');
				break;

			case "Coalesce":
				_ = _builder.Append("COALESCE(");
				for (var i = 0; i < node.Arguments.Count; i++)
				{
					if (i > 0)
						_ = _builder.Append(", ");
					_ = Visit(node.Arguments[i]);
				}

				_ = _builder.Append(')');
				break;

			case "Abs":
				_ = _builder.Append("ABS(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(')');
				break;

			case "Ceil":
				_ = _builder.Append("CEIL(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(')');
				break;

			case "Floor":
				_ = _builder.Append("FLOOR(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(')');
				break;

			case "Round":
				_ = _builder.Append("ROUND(");
				_ = Visit(node.Arguments[0]);
				if (node.Arguments.Count > 1)
				{
					_ = _builder.Append(", ");
					_ = Visit(node.Arguments[1]);
				}

				_ = _builder.Append(')');
				break;

			case "CidrMatch":
				_ = _builder.Append("CIDR_MATCH(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(", ");
				_ = Visit(node.Arguments[1]);
				_ = _builder.Append(')');
				break;

			default:
				throw new NotSupportedException($"ES|QL function {methodName} is not supported.");
		}

		return node;
	}

	private Expression VisitStringMethod(MethodCallExpression node)
	{
		var methodName = node.Method.Name;

		switch (methodName)
		{
			case "Contains":
				// string.Contains("x") → LIKE "*x*"
				_ = Visit(node.Object!);
				_ = _builder.Append(" LIKE ");
				var containsValue = GetConstantValue(node.Arguments[0]);
				_ = _builder.Append("\"*").Append(EscapeLikePattern(containsValue?.ToString() ?? "")).Append("*\"");
				break;

			case "StartsWith":
				// string.StartsWith("x") → LIKE "x*"
				_ = Visit(node.Object!);
				_ = _builder.Append(" LIKE ");
				var startsValue = GetConstantValue(node.Arguments[0]);
				_ = _builder.Append('"').Append(EscapeLikePattern(startsValue?.ToString() ?? "")).Append("*\"");
				break;

			case "EndsWith":
				// string.EndsWith("x") → LIKE "*x"
				_ = Visit(node.Object!);
				_ = _builder.Append(" LIKE ");
				var endsValue = GetConstantValue(node.Arguments[0]);
				_ = _builder.Append("\"*").Append(EscapeLikePattern(endsValue?.ToString() ?? "")).Append('"');
				break;

			case "ToLower":
			case "ToLowerInvariant":
				_ = _builder.Append("TO_LOWER(");
				_ = Visit(node.Object!);
				_ = _builder.Append(')');
				break;

			case "ToUpper":
			case "ToUpperInvariant":
				_ = _builder.Append("TO_UPPER(");
				_ = Visit(node.Object!);
				_ = _builder.Append(')');
				break;

			case "Trim":
				_ = _builder.Append("TRIM(");
				_ = Visit(node.Object!);
				_ = _builder.Append(')');
				break;

			case "Substring":
				_ = _builder.Append("SUBSTRING(");
				_ = Visit(node.Object!);
				_ = _builder.Append(", ");
				_ = Visit(node.Arguments[0]);
				if (node.Arguments.Count > 1)
				{
					_ = _builder.Append(", ");
					_ = Visit(node.Arguments[1]);
				}

				_ = _builder.Append(')');
				break;

			case "IsNullOrEmpty":
				_ = _builder.Append('(');
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" IS NULL OR ");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" == \"\")");
				break;

			case "IsNullOrWhiteSpace":
				_ = _builder.Append('(');
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" IS NULL OR TRIM(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(") == \"\")");
				break;

			case "get_Chars":
				// string[i] → SUBSTRING(s, i+1, 1)
				_ = _builder.Append("SUBSTRING(");
				_ = Visit(node.Object!);
				_ = _builder.Append(", ");
				// Add 1 for 1-based indexing in ES|QL
				var index = GetConstantValue(node.Arguments[0]);
				if (index is int idx)
					_ = _builder.Append(idx + 1);
				else
				{
					_ = _builder.Append('(');
					_ = Visit(node.Arguments[0]);
					_ = _builder.Append(") + 1");
				}

				_ = _builder.Append(", 1)");
				break;

			default:
				throw new NotSupportedException($"String method {methodName} is not supported.");
		}

		return node;
	}

	private Expression VisitDateTimeMethod(MethodCallExpression node)
	{
		var methodName = node.Method.Name;

		switch (methodName)
		{
			case "AddDays":
			case "AddHours":
			case "AddMinutes":
			case "AddSeconds":
			case "AddMilliseconds":
				// DateTime arithmetic
				_ = _builder.Append('(');
				_ = Visit(node.Object!);
				var amount = GetConstantValue(node.Arguments[0]);
				var unit = methodName.Replace("Add", "").ToLowerInvariant();
				_ = amount is double d and < 0
					? _builder.Append(CultureInfo.InvariantCulture, $" - {Math.Abs(d)} {unit}")
					: _builder.Append(CultureInfo.InvariantCulture, $" + {amount} {unit}");
				_ = _builder.Append(')');
				break;

			default:
				throw new NotSupportedException($"DateTime method {methodName} is not supported.");
		}

		return node;
	}

	private Expression VisitContains(MethodCallExpression node)
	{
		// Enumerable.Contains(collection, value) → value IN (...)
		var collection = GetConstantValue(node.Arguments[0]) as IEnumerable;
		var valueExpr = node.Arguments[1];

		_ = Visit(valueExpr);
		_ = _builder.Append(" IN (");

		var first = true;
		foreach (var item in collection!)
		{
			if (!first)
				_ = _builder.Append(", ");
			_ = _builder.Append(EsqlTypeMapper.FormatValue(item));
			first = false;
		}

		_ = _builder.Append(')');

		return node;
	}

	private Expression VisitListContains(MethodCallExpression node)
	{
		// list.Contains(value) → value IN (...)
		var collection = GetConstantValue(node.Object!) as IEnumerable;
		var valueExpr = node.Arguments[0];

		_ = Visit(valueExpr);
		_ = _builder.Append(" IN (");

		var first = true;
		foreach (var item in collection!)
		{
			if (!first)
				_ = _builder.Append(", ");
			_ = _builder.Append(EsqlTypeMapper.FormatValue(item));
			first = false;
		}

		_ = _builder.Append(')');

		return node;
	}

	private static string GetOperator(ExpressionType nodeType) =>
		nodeType switch
		{
			ExpressionType.Equal => "==",
			ExpressionType.NotEqual => "!=",
			ExpressionType.LessThan => "<",
			ExpressionType.LessThanOrEqual => "<=",
			ExpressionType.GreaterThan => ">",
			ExpressionType.GreaterThanOrEqual => ">=",
			ExpressionType.AndAlso => "AND",
			ExpressionType.OrElse => "OR",
			ExpressionType.Add => "+",
			ExpressionType.Subtract => "-",
			ExpressionType.Multiply => "*",
			ExpressionType.Divide => "/",
			ExpressionType.Modulo => "%",
			_ => throw new NotSupportedException($"Operator {nodeType} is not supported.")
		};

	private static object? GetConstantValue(Expression expression) =>
		expression switch
		{
			ConstantExpression constant => constant.Value,
			MemberExpression member when member.Expression is ConstantExpression ce =>
				GetMemberValue(member, ce.Value),
			UnaryExpression { NodeType: ExpressionType.Convert, Operand: ConstantExpression c } => c.Value,
			_ => Expression.Lambda(expression).Compile().DynamicInvoke()
		};

	private static object? GetMemberValue(MemberExpression member, object? instance) =>
		member.Member switch
		{
			FieldInfo field => field.GetValue(instance),
			PropertyInfo property => property.GetValue(instance),
			_ => throw new NotSupportedException($"Member type {member.Member.GetType()} is not supported.")
		};

	private static object? GetStaticMemberValue(MemberExpression member) =>
		member.Member switch
		{
			FieldInfo field => field.GetValue(null),
			PropertyInfo property => property.GetValue(null),
			_ => throw new NotSupportedException($"Static member type {member.Member.GetType()} is not supported.")
		};

	private static string EscapeLikePattern(string value) =>
		// Escape special characters in LIKE patterns
		value
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("*", "\\*")
			.Replace("?", "\\?");
}
