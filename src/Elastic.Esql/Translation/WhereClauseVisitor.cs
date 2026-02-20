// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.Formatting;
using Elastic.Esql.Functions;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ predicate expressions to ES|QL WHERE conditions.
/// </summary>
internal sealed class WhereClauseVisitor(EsqlTranslationContext context) : ExpressionVisitor
{
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));
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
		var enumComparison = TryGetEnumComparison(node);
		if (enumComparison.HasValue && !IsSpecialEnumAccess(enumComparison.Value.MemberSide.Member) && ShouldFormatEnumAsString(enumComparison.Value.EnumType))
		{
			_ = Visit(enumComparison.Value.MemberSide);
			var op = GetOperator(node.NodeType);
			_ = _builder.Append(' ').Append(op).Append(' ');

			var constant = enumComparison.Value.ConstantSide;
			var constantValue = ExpressionConstantResolver.Resolve(constant);

			if (constant is MemberExpression member)
			{
				var name = member.Member.Name;
				var value = GetEnumStringValue(enumComparison.Value.EnumType, constantValue);

				_ = _builder.Append(_context.GetValueOrParameterName(name, value));
			}
			else
				_ = _builder.Append(EsqlFormatting.FormatValue(constantValue));
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

	/// <summary>
	/// Inspects a <see cref="BinaryExpression"/> and determines whether it represents an enum comparison. If so, returns the enum type, the member site
	/// expression (the property/field being compared), and the constant enum value. Handles both regular and nullable enums.
	/// </summary>
	private static (Type EnumType, MemberExpression MemberSide, Expression ConstantSide)? TryGetEnumComparison(BinaryExpression binary)
	{
		// TODO: We can probably make this more robust by explicitly looking for the parametrized member access as the source of truth for the enum type.

		if (binary.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual
			or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
			or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual))
			return null;

		// Try both orientations: member == constant and constant == member.
		return TryMatch(binary.Left, binary.Right) ?? TryMatch(binary.Right, binary.Left);

		static (Type EnumType, MemberExpression MemberSide, Expression ConstantSide)? TryMatch(Expression candidateMember, Expression candidateConstant)
		{
			var memberSide = candidateMember.UnwrapConvertExpressions();
			var constantSide = candidateConstant.UnwrapConvertExpressions();

			// Resolve the enum type from whichever side actually has it.
			// The member side is authoritative, but for `Nullable<TEnum> == null` the constant side may be typed differently.
			var enumType = GetEnumType(memberSide.Type);
			if (enumType is null)
				return null;

			// The member side must be a member access.
			if (memberSide is not MemberExpression memberExpression)
				return null;

			// The constant side must be a static- or closure-rooted expression that can be resolved to a value.
			// Cases where both sides are dependent on the input lambda parameter are dealt with as non-enum comparisons and don't require special handling.
			if (!constantSide.SupportsEvaluation())
				return null;

			return (enumType, memberExpression, constantSide);
		}

		static Type? GetEnumType(Type type)
		{
			var candidate = Nullable.GetUnderlyingType(type) ?? type;

			return candidate.IsEnum ? candidate : null;
		}
	}

	private static bool IsSpecialEnumAccess(MemberInfo member)
	{
		// DateTime/DateTimeOffset properties like DayOfWeek return enums but translate to
		// DATE_EXTRACT which produces integers — don't treat these as enum comparisons
		var declaringType = member.DeclaringType;
		return declaringType == typeof(DateTime) || declaringType == typeof(DateTimeOffset);
	}

	private static bool ShouldFormatEnumAsString(Type enumType)
	{
		// TODO: This is not always the case. Users can choose to store enums as integers in Elasticsearch.
		//       For now, we require enums to be stored as strings for simplicity.

		// Always format enums as strings since keyword fields expect strings.
		// The JsonStringEnumConverter attribute on the property ensures the data is stored as strings.
		_ = enumType; // Suppress unused parameter warning
		return true;
	}

	private static string? GetEnumStringValue(Type enumType, object? value)
	{
		if (value == null)
			return null;

		// Convert the value to the enum and get its name
		var enumValue = Enum.ToObject(enumType, value);
		return Enum.GetName(enumType, enumValue) ?? value.ToString() ?? "";
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
			_ = _builder.Append(_context.GetValueOrParameterName(node.Member.Name, value));
			return node;
		}

		// Check if this is a nested member access on a captured variable
		if (node.Expression is MemberExpression innerMember &&
			innerMember.Expression is ConstantExpression innerConstant)
		{
			var innerValue = GetMemberValue(innerMember, innerConstant.Value);
			var value = GetMemberValue(node, innerValue);
			_ = _builder.Append(_context.GetValueOrParameterName(node.Member.Name, value));
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

			// Math constants: Math.E, Math.PI, Math.Tau
			if (declaringType == typeof(Math))
			{
				var mathConst = EsqlFunctionTranslator.TryTranslateMathConstant(memberName);
				if (mathConst != null)
				{
					_ = _builder.Append(mathConst);
					return node;
				}
			}

			// For other static members, evaluate the value
			var value = GetStaticMemberValue(node);
			_ = _builder.Append(EsqlFormatting.FormatValue(value));
			return node;
		}

		// Handle string.Length property → LENGTH(field)
		if (node.Member.DeclaringType == typeof(string) && node.Member.Name == "Length")
		{
			_ = _builder.Append("LENGTH(");
			_ = Visit(node.Expression!);
			_ = _builder.Append(')');
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
		var fieldName = _context.FieldMetadataResolver.GetFieldName(node.Member.DeclaringType!, node.Member);
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
				_context.FieldMetadataResolver.GetFieldName(member.Member.DeclaringType!, member.Member),
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
		_ = _builder.Append(EsqlFormatting.FormatValue(node.Value));
		return node;
	}

	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		var methodName = node.Method.Name;
		var declaringType = node.Method.DeclaringType;

		// MultiField extension: l.Field.MultiField("keyword")
		if (declaringType == typeof(GeneralPurposeExtensions) && methodName == "MultiField")
		{
			_ = _builder.Append(node.ResolveFieldName(_context.FieldMetadataResolver));
			return node;
		}

		// Check for EsqlFunctions marker methods
		if (declaringType == typeof(EsqlFunctions))
			return VisitEsqlFunction(node);

		// String methods
		if (declaringType == typeof(string))
			return VisitStringMethod(node);

		// Math methods
		if (declaringType == typeof(Math))
			return VisitMathMethod(node);

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
		_ = _builder.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}", value, unit);
		return Expression.Empty();
	}

	private Expression VisitEsqlFunction(MethodCallExpression node)
	{
		var methodName = node.Method.Name;

		switch (methodName)
		{
			case "Match":
			case "MatchPhrase":
				_ = _builder.Append(methodName == "Match" ? "MATCH(" : "MATCH_PHRASE(");
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(", ");
				_ = Visit(node.Arguments[1]);
				_ = _builder.Append(')');
				return node;

			case "IsNull":
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" IS NULL");
				return node;

			case "IsNotNull":
				_ = Visit(node.Arguments[0]);
				_ = _builder.Append(" IS NOT NULL");
				return node;
		}

		// Delegate to shared translator
		var result = EsqlFunctionTranslator.TryTranslate(methodName, TranslateSubExpression, node.Arguments);
		if (result != null)
		{
			_ = _builder.Append(result);
			return node;
		}

		throw new NotSupportedException($"ES|QL function {methodName} is not supported.");
	}

	private Expression VisitMathMethod(MethodCallExpression node)
	{
		var methodName = node.Method.Name;
		var result = EsqlFunctionTranslator.TryTranslateMath(methodName, TranslateSubExpression, node.Arguments);
		if (result != null)
		{
			_ = _builder.Append(result);
			return node;
		}

		throw new NotSupportedException($"Math method {methodName} is not supported.");
	}

	private string TranslateSubExpression(Expression expression)
	{
		var saved = _builder.ToString();
		_ = _builder.Clear();
		_ = Visit(expression);
		var result = _builder.ToString();
		_ = _builder.Clear().Append(saved);
		return result;
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
				// Try shared string translator for TrimStart, TrimEnd, Replace, IndexOf, Split, etc.
				if (node.Object != null)
				{
					var target = TranslateSubExpression(node.Object);
					var result = EsqlFunctionTranslator.TryTranslateString(methodName, TranslateSubExpression, target, node.Arguments);
					if (result != null)
					{
						_ = _builder.Append(result);
						break;
					}
				}

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
					? _builder.AppendFormat(CultureInfo.InvariantCulture, " - {0} {1}", Math.Abs(d), unit)
					: _builder.AppendFormat(CultureInfo.InvariantCulture, " + {0} {1}", amount, unit);
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
			_ = _builder.Append(EsqlFormatting.FormatValue(item));
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
			_ = _builder.Append(EsqlFormatting.FormatValue(item));
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

#if NET8_0_OR_GREATER
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Expression compilation fallback for constant evaluation.")]
#endif
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

	private static bool IsNullConstant(Expression expression) =>
		expression is ConstantExpression { Value: null };

	private static string EscapeLikePattern(string value) =>
		// Escape special characters in LIKE patterns
		value
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("*", "\\*")
			.Replace("?", "\\?");
}
