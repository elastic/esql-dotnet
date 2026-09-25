// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.Formatting;
using Elastic.Esql.Functions;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ predicate expressions to ES|QL WHERE conditions.
/// </summary>
internal sealed class WhereClauseVisitor(EsqlTranslationContext context) : ExpressionVisitor
{
	/// <summary>
	/// How many values of a collection an Any over it may test, each with its own MATCH.
	/// Each one adds a level to the expression Elasticsearch parses, and it stops
	/// accepting them past this.
	/// </summary>
	private const int MaxMatchedValues = 256;

	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));
	private readonly StringBuilder _builder = new();
	private MemberInfo? _comparisonPropertyContext;

	// Values resolved by the IS NULL rewrite, keyed by the member node so VisitMember can reuse
	// them instead of evaluating the same closure chain (and its getters) a second time.
	private readonly Dictionary<Expression, object?> _resolvedCaptures = [];

	// MATCH is emitted once per value of a captured collection, and the commands before the
	// WHERE are the same for each: their position is checked once
	private bool _matchPositionChecked;

	private enum ElementPredicateKind
	{
		Equal,
		In,
		StartsWith,
		EndsWith,
		Contains,
		GreaterThan,
		GreaterThanOrEqual,
		LessThan,
		LessThanOrEqual
	}

	private readonly record struct ElementPredicate(
		ElementPredicateKind Kind,
		IReadOnlyList<Expression> Values,
		bool Negated);

	/// <summary>
	/// Translates a predicate expression to an ES|QL condition string.
	/// </summary>
	public string Translate(Expression expression)
	{
		_ = _builder.Clear();
		_resolvedCaptures.Clear();
		_matchPositionChecked = false;
		_ = Visit(expression);
		return _builder.ToString();
	}

	protected override Expression VisitBinary(BinaryExpression node)
	{
		if (TryVisitRootNullGuard(node))
			return node;

		if (TryVisitStringComparison(node))
			return node;

		if (TryVisitMultiValueComparison(node))
			return node;

		if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
		{
			var nullOp = node.NodeType == ExpressionType.Equal ? "IS NULL" : "IS NOT NULL";

			if (ResolvesToNull(node.Right))
			{
				AppendComparisonOperand(node.Left, parentIsEquality: true);
				_ = _builder.Append(' ').Append(nullOp);
				return node;
			}

			if (ResolvesToNull(node.Left))
			{
				AppendComparisonOperand(node.Right, parentIsEquality: true);
				_ = _builder.Append(' ').Append(nullOp);
				return node;
			}
		}

		// Must run after null handling (IS NULL wins) and before generic enum comparison (which would emit C# ordinals).
		var dayOfWeekComparison = EsqlFunctionTranslator.TryGetDayOfWeekComparison(node);
		if (dayOfWeekComparison.HasValue)
		{
			_ = Visit(dayOfWeekComparison.Value.DateMember);
			_ = _builder.Append(' ').Append(EsqlFunctionTranslator.GetOperator(node.NodeType)).Append(' ').Append(dayOfWeekComparison.Value.IsoDayNumber);
			return node;
		}

		// Parenthesize logical and arithmetic nodes so the C# expression tree grouping survives;
		// a flat rendering would let ES|QL re-associate operands by its own precedence rules.
		var needsParentheses = node.NodeType
			is ExpressionType.AndAlso or ExpressionType.OrElse
			or ExpressionType.Add or ExpressionType.Subtract
			or ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo;

		if (needsParentheses)
			_ = _builder.Append('(');

		var enumComparison = TryGetEnumComparison(node);
		if (enumComparison.HasValue && !IsSpecialEnumAccess(enumComparison.Value.MemberSide.Member))
		{
			var propertyMember = enumComparison.Value.MemberSide.Member;
			_ = Visit(enumComparison.Value.MemberSide);

			// The member side is always emitted first; when it originally sat on the right,
			// relational operators must be mirrored to preserve the predicate.
			var op = EsqlFunctionTranslator.GetOperator(enumComparison.Value.Swapped ? MirrorComparison(node.NodeType) : node.NodeType);
			_ = _builder.Append(' ').Append(op).Append(' ');

			var constant = enumComparison.Value.ConstantSide;
			var constantValue = ExpressionConstantResolver.Resolve(constant);
			var enumValue = constantValue is not null ? Enum.ToObject(enumComparison.Value.EnumType, constantValue) : null;

			_ = constant is MemberExpression member
				? _builder.Append(_context.GetValueOrParameterName(member.Member.Name, enumValue, propertyMember))
				: _builder.Append(_context.FormatValue(enumValue, propertyMember));
		}
		else
		{
			var parentIsEquality = node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual;
			_comparisonPropertyContext = ExtractEntityPropertyMember(node);
			AppendComparisonOperand(node.Left, parentIsEquality);
			var op = EsqlFunctionTranslator.GetOperator(node.NodeType);
			_ = _builder.Append(' ').Append(op).Append(' ');
			AppendComparisonOperand(node.Right, parentIsEquality);
			_comparisonPropertyContext = null;
		}

		if (needsParentheses)
			_ = _builder.Append(')');

		return node;
	}

	/// <summary>
	/// A comparison nested as an equality operand must keep its own parentheses;
	/// ES|QL misparses the flat form (e.g. <c>a > b == flag</c>).
	/// </summary>
	private void AppendComparisonOperand(Expression operand, bool parentIsEquality)
	{
		var isNestedComparison = parentIsEquality && operand.UnwrapConvertExpressions() is BinaryExpression
		{
			NodeType: ExpressionType.Equal or ExpressionType.NotEqual
				or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
				or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
		};

		if (isNestedComparison)
			_ = _builder.Append('(');

		_ = Visit(operand);

		if (isNestedComparison)
			_ = _builder.Append(')');
	}

	/// <summary>
	/// Inspects a <see cref="BinaryExpression"/> and determines whether it represents an enum comparison. If so, returns the enum type, the member site
	/// expression (the property/field being compared), the constant enum value, and whether the member side originally sat on the right-hand side.
	/// Handles both regular and nullable enums.
	/// </summary>
	private static (Type EnumType, MemberExpression MemberSide, Expression ConstantSide, bool Swapped)? TryGetEnumComparison(BinaryExpression binary)
	{
		// TODO: We can probably make this more robust by explicitly looking for the parametrized member access as the source of truth for the enum type.

		if (binary.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual
			or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
			or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual))
			return null;

		// Try both orientations: member == constant and constant == member.
		return TryMatch(binary.Left, binary.Right, swapped: false) ?? TryMatch(binary.Right, binary.Left, swapped: true);

		static (Type EnumType, MemberExpression MemberSide, Expression ConstantSide, bool Swapped)? TryMatch(
			Expression candidateMember,
			Expression candidateConstant,
			bool swapped
		)
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

			return (enumType, memberExpression, constantSide, swapped);
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

	/// <summary>
	/// Extracts the entity property <see cref="MemberInfo"/> from a binary comparison so that
	/// property-level <see cref="System.Text.Json.Serialization.JsonConverterAttribute"/> can
	/// be respected when serializing the compared value.
	/// </summary>
	private static MemberInfo? ExtractEntityPropertyMember(BinaryExpression node)
	{
		if (node.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual
			or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
			or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual))
			return null;

		return EntityPropertyMember(node.Left) ?? EntityPropertyMember(node.Right);
	}

	private static MemberInfo? EntityPropertyMember(Expression expr)
	{
		var unwrapped = expr.UnwrapConvertExpressions();
		if (unwrapped is MemberExpression member && ExpressionTranslationHelpers.IsRootedInParameter(member))
			return member.Member;

		return null;
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
				// Implicit/explicit conversion to a DenseVector<T> from a closure-captured
				// T[] / ReadOnlyMemory<T>. Resolve the converted value (the implicit operator
				// is invoked by ExpressionConstantResolver) and emit as a parameter / inline literal.
				if (TryEmitVectorConvert(node))
					return node;

				// Just visit the operand, ES|QL handles type coercion
				_ = Visit(node.Operand);
				break;

			default:
				throw new NotSupportedException($"Unary operator {node.NodeType} is not supported.");
		}

		return node;
	}

	private bool TryEmitVectorConvert(UnaryExpression node)
	{
		if (!DenseVectorTypeHelper.TryEmitDenseVectorLiteral(node, _context, out var literal))
			return false;

		_ = _builder.Append(literal);
		return true;
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		// Closure-rooted member paths (captured variables and member chains of any depth on
		// captured objects) resolve to a constant value and emit as a parameter or inline literal.
		if (node.Expression.IsClosureRooted())
		{
			if (!_resolvedCaptures.TryGetValue(node, out var value))
				value = ExpressionConstantResolver.Resolve(node);
			else
				_ = _resolvedCaptures.Remove(node);
			_ = _builder.Append(_context.GetValueOrParameterName(node.Member.Name, value, _comparisonPropertyContext));
			_comparisonPropertyContext = null;
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
				var translated = EsqlFunctionTranslator.TryTranslateStaticDateProperty(memberName);
				if (translated is not null)
				{
					_ = _builder.Append(translated);
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

			// EsqlMetadata.* marker access -> emit underscore-prefixed ES|QL identifier.
			if (declaringType == typeof(EsqlMetadata))
			{
				_ = _builder.Append(_context.ResolveMetadataMemberOrThrow(memberName));
				return node;
			}

			// For other static members, evaluate the value
			var value = GetStaticMemberValue(node);
			_ = _builder.Append(_context.FormatValue(value));
			return node;
		}

		// Handle string.Length property → LENGTH(field)
		if (node.Member.DeclaringType == typeof(string) && node.Member.Name == "Length")
		{
			_ = _builder.Append("LENGTH(");
			_ = Visit(node.Expression);
			_ = _builder.Append(')');
			return node;
		}

		// Check for DateTime/DateTimeOffset property access (Year, Month, Day, etc.)
		if (node.Member.DeclaringType == typeof(DateTime) || node.Member.DeclaringType == typeof(DateTimeOffset))
		{
			var dateExpr = TranslateDateTimeExpression(node.Expression);
			var translated = EsqlFunctionTranslator.TryTranslateDateMember(node.Member.Name, dateExpr);
			if (translated != null)
			{
				_ = _builder.Append(translated);
				return node;
			}
		}

		// Regular field access
		var fieldName = ResolveFieldPath(node);
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
				ResolveFieldPath(member),
			MethodCallExpression methodCall when methodCall.Method.DeclaringType == typeof(EsqlFunctions) =>
				TranslateEsqlFunctionForDateTime(methodCall),
			_ => throw new NotSupportedException($"Expression type {expression.GetType().Name} is not supported for DateTime property access.")
		};

	private string ResolveFieldPath(MemberExpression member)
	{
		var remainingPath = member.ResolveFieldName(_context.Metadata);

		foreach (var prefix in GetTransparentIdentifierPrefixes(member))
		{
			var prefixWithDot = $"{prefix}.";
			if (!remainingPath.StartsWith(prefixWithDot, StringComparison.Ordinal))
				break;

			remainingPath = remainingPath[prefixWithDot.Length..];
		}

		return remainingPath;
	}

	private IEnumerable<string> GetTransparentIdentifierPrefixes(MemberExpression member)
	{
		var chain = ExpressionTranslationHelpers.GetMemberChainFromRoot(member);
		foreach (var chainedMember in chain)
		{
			var declaringType = chainedMember.Member.DeclaringType;
			if (declaringType is null || !declaringType.IsDefined(typeof(CompilerGeneratedAttribute), false))
				yield break;

			if (_context.IsTrackedAnonymousType(declaringType))
				yield break;

			yield return _context.ResolveFieldName(declaringType, chainedMember.Member);
		}
	}

	private string TranslateStaticDateTimeProperty(MemberExpression member)
	{
		var memberName = member.Member.Name;
		return EsqlFunctionTranslator.TryTranslateStaticDateProperty(memberName)
			?? throw new NotSupportedException($"DateTime static property {memberName} is not supported.");
	}

	private string TranslateEsqlFunctionForDateTime(MethodCallExpression methodCall)
	{
		var methodName = methodCall.Method.Name;
		var translated = EsqlFunctionTranslator.TryTranslateMethodCall(methodCall, TranslateDateTimeExpression);
		return translated ?? throw new NotSupportedException($"EsqlFunction {methodName} is not supported in DateTime context.");
	}

	protected override Expression VisitNew(NewExpression node)
	{
		// Fold inline constructor calls (e.g. new DateTime(2024, 1, 1)) to their value; the base
		// visitor would render each ctor argument individually, concatenating a corrupt literal.
		var value = ExpressionConstantResolver.Resolve(node);
		_ = _builder.Append(_context.FormatValue(value, _comparisonPropertyContext));
		_comparisonPropertyContext = null;
		return node;
	}

	protected override Expression VisitConstant(ConstantExpression node)
	{
		_ = _builder.Append(_context.FormatValue(node.Value, _comparisonPropertyContext));
		_comparisonPropertyContext = null;
		return node;
	}

	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		var methodName = node.Method.Name;
		var declaringType = node.Method.DeclaringType;

		// MultiField extension: l.Field.MultiField("keyword")
		if (declaringType == typeof(GeneralPurposeExtensions) && methodName == "MultiField")
		{
			_ = _builder.Append(node.ResolveFieldName(_context.Metadata));
			return node;
		}

		// Check for EsqlFunctions marker methods
		if (declaringType == typeof(EsqlFunctions))
		{
			// IsNull and IsNotNull take the field to test, and the row is not one: it has
			// no field name to put in front of the operator, so the marker would emit the
			// operator with nothing before it.
			if (methodName is nameof(EsqlFunctions.IsNull) or nameof(EsqlFunctions.IsNotNull)
				&& node.Arguments is [{ } only]
				&& only.UnwrapConvertExpressions() is ParameterExpression)
			{
				throw new NotSupportedException(
					$"EsqlFunctions.{methodName} on the row itself is not supported: pass the field "
					+ "to test, since the row has no field name of its own to put in front of the operator.");
			}

			return VisitEsqlFunction(node);
		}

		// String methods
		if (declaringType == typeof(string))
			return VisitStringMethod(node);

		// Math methods
		if (declaringType == typeof(Math))
			return VisitMathMethod(node);

		// Multi-value field predicates: tags.Any(t => t == "x"), tags.Contains("x").
		// Checked before the constant-collection IN translation, because there the
		// collection is a captured constant while here it is a document field.
		if (TryVisitMultiValueField(node))
			return node;

		if (methodName == "Contains" && TryVisitCollectionContains(node))
			return node;

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
		var count = Convert.ToDouble(GetConstantValue(node.Arguments[0]), CultureInfo.InvariantCulture);

		var (interval, unit) = methodName switch
		{
			"FromDays" => (TimeSpan.FromDays(count), "days"),
			"FromHours" => (TimeSpan.FromHours(count), "hours"),
			"FromMinutes" => (TimeSpan.FromMinutes(count), "minutes"),
			"FromSeconds" => (TimeSpan.FromSeconds(count), "seconds"),
			"FromMilliseconds" => (TimeSpan.FromMilliseconds(count), "milliseconds"),
			_ => throw new NotSupportedException($"TimeSpan method {methodName} is not supported.")
		};

		// ES|QL duration counts are integers. An integral argument keeps the unit the caller wrote; a
		// fractional one is re-expressed in whole milliseconds or rejected, exactly like a captured TimeSpan.
		var literal = Math.Floor(count) == count
			? string.Format(CultureInfo.InvariantCulture, "{0} {1}", count, unit)
			: EsqlFormatting.FormatTimeSpanRaw(interval);
		_ = _builder.Append(literal);

		return Expression.Empty();
	}

	private Expression VisitEsqlFunction(MethodCallExpression node)
	{
		var methodName = node.Method.Name;
		var result = EsqlFunctionTranslator.TryTranslateMethodCall(node, TranslateSubExpression);
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
		var result = EsqlFunctionTranslator.TryTranslateMethodCall(node, TranslateSubExpression);
		if (result != null)
		{
			_ = _builder.Append(result);
			return node;
		}

		throw new NotSupportedException($"Math method {methodName} is not supported.");
	}

	private string TranslateSubExpression(Expression expression)
	{
		// Translate into the builder tail and truncate afterwards, so nested
		// arguments never re-copy the already accumulated condition prefix.
		var start = _builder.Length;
		_ = Visit(expression);
		var result = _builder.ToString(start, _builder.Length - start);
		_builder.Length = start;
		return result;
	}

	private Expression VisitStringMethod(MethodCallExpression node)
	{
		var methodName = node.Method.Name;

		EsqlFunctionTranslator.ThrowIfUnsupportedStringComparison(node);

		switch (methodName)
		{
			case "Contains":
				// string.Contains("x") → LIKE "*x*"
				_ = Visit(node.Object);
				_ = _builder.Append(" LIKE ");
				var containsValue = RequireSearchValue(node, methodName);
				_ = _builder.Append(EsqlFormatting.FormatString($"*{EscapeLikePattern(containsValue)}*"));
				break;

			case "StartsWith":
				// string.StartsWith("x") → LIKE "x*"
				_ = Visit(node.Object);
				_ = _builder.Append(" LIKE ");
				var startsValue = RequireSearchValue(node, methodName);
				_ = _builder.Append(EsqlFormatting.FormatString($"{EscapeLikePattern(startsValue)}*"));
				break;

			case "EndsWith":
				// string.EndsWith("x") → LIKE "*x"
				_ = Visit(node.Object);
				_ = _builder.Append(" LIKE ");
				var endsValue = RequireSearchValue(node, methodName);
				_ = _builder.Append(EsqlFormatting.FormatString($"*{EscapeLikePattern(endsValue)}"));
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
				var indexerTarget = node.Object ?? throw new NotSupportedException("The string indexer requires an instance.");
				var indexer = EsqlFunctionTranslator.TranslateStringIndexer(TranslateSubExpression(indexerTarget), node.Arguments[0], TranslateSubExpression);
				_ = _builder.Append(indexer);
				break;

			case "CompareTo":
			case "Compare":
			case "CompareOrdinal":
				// an ordering only exists inside a comparison against zero, which the binary
				// visitor rewrites into a direct comparison, and only for the ordinal forms
				throw new NotSupportedException(
					$"String method {methodName} is only supported as string.CompareOrdinal(a, b) "
					+ "or string.Compare(a, b, StringComparison.Ordinal) inside an ordering "
					+ "comparison against zero, for example string.CompareOrdinal(a, b) > 0.");

			default:
				var result = EsqlFunctionTranslator.TryTranslateMethodCall(node, TranslateSubExpression);
				if (result != null)
				{
					_ = _builder.Append(result);
					break;
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
				_ = Visit(node.Object);
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

	private bool TryVisitCollectionContains(MethodCallExpression node)
	{
		if (TryGetContainsArguments(node, out var valueExpression, out var collection))
		{
			AppendContainsCollection(valueExpression, collection);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Whether the call is a Contains overload that takes an equality comparer, as its
	/// last parameter. The comparison emitted is the one Elasticsearch performs, which
	/// the comparer would not follow.
	/// </summary>
	private static bool TakesAnEqualityComparer(MethodCallExpression node) =>
		node.Method.Name == "Contains"
		&& node.Method.GetParameters() is [.., { ParameterType: { IsGenericType: true } last }]
		&& last.GetGenericTypeDefinition() == typeof(IEqualityComparer<>);

	private static NotSupportedException ContainsWithAnEqualityComparer() => new(
		"Contains with an equality comparer is not supported: the emitted comparison is the one "
		+ "Elasticsearch performs, which the comparer would not follow. Call Contains without one.");

	private static bool TryGetContainsArguments(MethodCallExpression node, out Expression valueExpression, out IEnumerable? collection)
	{
		valueExpression = null!;
		collection = null;

		if (TakesAnEqualityComparer(node))
			throw ContainsWithAnEqualityComparer();

		if (node.Method.IsStatic)
		{
			if (node.Method.DeclaringType == typeof(Enumerable) && node.Arguments.Count >= 2)
			{
				valueExpression = node.Arguments[1];
				return TryGetCollectionValue(node.Arguments[0], out collection);
			}

			if (node.Method.DeclaringType == typeof(MemoryExtensions) && node.Arguments.Count >= 2)
			{
				var source = TryUnwrapMemoryExtensionsSource(node.Arguments[0]);
				if (source is null)
					return false;

				valueExpression = node.Arguments[1];
				return TryGetCollectionValue(source, out collection);
			}

			return false;
		}

		if (node.Object is null || node.Arguments.Count != 1 || !IsNonStringEnumerable(node.Object.Type))
			return false;

		valueExpression = node.Arguments[0];
		return TryGetCollectionValue(node.Object, out collection);
	}

	private static Expression? TryUnwrapMemoryExtensionsSource(Expression expression)
	{
		var current = expression;

		while (true)
		{
			// Handle implicit/explicit conversions (e.g., array -> ReadOnlySpan<T>)
			while (current is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
				current = unary.Operand;

			if (current is not MethodCallExpression methodCall || methodCall.Arguments.Count == 0)
				break;

			// Handle explicit AsSpan(...) wrappers emitted in expression trees.
			if (methodCall.Method.DeclaringType == typeof(MemoryExtensions) && methodCall.Method.Name == "AsSpan")
			{
				current = methodCall.Arguments[0];
				continue;
			}

			// Handle op_Implicit wrappers used for array -> ReadOnlySpan<T> conversions.
			if (methodCall.Method.Name == "op_Implicit")
			{
				current = methodCall.Arguments[0];
				continue;
			}

			break;
		}

		return IsNonStringEnumerable(current.Type) ? current : null;
	}

	private static bool IsNonStringEnumerable(Type type) =>
		type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

	private static bool TryGetCollectionValue(Expression expression, out IEnumerable? collection)
	{
		collection = null;

		if (!IsNonStringEnumerable(expression.Type))
			return false;

		try
		{
			collection = GetConstantValue(expression) as IEnumerable;
			return true;
		}
		catch (NotSupportedException)
		{
			return false;
		}
	}

	private void AppendContainsCollection(Expression valueExpression, IEnumerable? collection)
	{
		// Enumerable/List Contains over an empty set is always false.
		if (collection is null)
			throw new ArgumentNullException(nameof(collection), "Collection used with Contains cannot be null.");

		var values = collection.Cast<object?>().ToList();
		if (values.Count == 0)
		{
			_ = _builder.Append("false");
			return;
		}

		_ = Visit(valueExpression);
		_ = _builder.Append(" IN (");

		for (var i = 0; i < values.Count; i++)
		{
			if (i > 0)
				_ = _builder.Append(", ");

			_ = _builder.Append(_context.FormatValue(values[i]));
		}

		_ = _builder.Append(')');
	}

	/// <summary>Mirrors a relational operator for a comparison whose operands were swapped into member-first order.</summary>
	private static ExpressionType MirrorComparison(ExpressionType nodeType) =>
		nodeType switch
		{
			ExpressionType.LessThan => ExpressionType.GreaterThan,
			ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
			ExpressionType.GreaterThan => ExpressionType.LessThan,
			ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
			_ => nodeType
		};

	private static object? GetConstantValue(Expression expression)
	{
		try
		{
			return ExpressionConstantResolver.Resolve(expression);
		}
		catch (NotSupportedException ex)
		{
			throw new NotSupportedException($"Expression '{expression}' is not supported for constant evaluation.", ex);
		}
	}

	private static object? GetStaticMemberValue(MemberExpression member) =>
		member.Member switch
		{
			FieldInfo field => field.GetValue(null),
			PropertyInfo property => property.GetValue(null),
			_ => throw new NotSupportedException($"Static member type {member.Member.GetType()} is not supported.")
		};

	private static bool IsNullConstant(Expression expression) =>
		expression is ConstantExpression { Value: null };

	/// <summary>
	/// True when the operand is a syntactic null or a closure/static-rooted expression whose
	/// runtime value is null. Rendering such a value inline would emit a dead <c>== null</c>
	/// comparison (always null in ES|QL) instead of the intended <c>IS NULL</c>.
	/// </summary>
	private bool ResolvesToNull(Expression expression)
	{
		if (IsNullConstant(expression))
			return true;

		if (expression is ConstantExpression || !expression.SupportsEvaluation())
			return false;

		var target = expression.UnwrapConvertExpressions();

		// A closure-rooted chain runs exactly once: a failure here is the failure VisitMember would
		// raise anyway, so it propagates instead of re-running the chain's getters.
		if (target.IsClosureRooted())
		{
			var value = ExpressionConstantResolver.Resolve(target);
			_resolvedCaptures[target] = value;
			return value is null;
		}

		try
		{
			return ExpressionConstantResolver.Resolve(target) is null;
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or TargetInvocationException)
		{
			// Static markers such as EsqlMetadata throw on evaluation and keep their dedicated
			// translation. Anything else is a real bug: propagate.
			return false;
		}
	}

	// C# throws for a null search value; rendering it as an empty pattern would silently turn the
	// predicate into a match-all LIKE.
	private static string RequireSearchValue(MethodCallExpression node, string methodName) =>
		GetConstantValue(node.Arguments[0])?.ToString()
			?? throw new NotSupportedException($"The search value passed to '{methodName}' must not be null; the LIKE pattern would match everything.");

	/// <summary>
	/// "p != null" on the lambda parameter itself: the document is never null, and
	/// there is no field to put in front of IS NOT NULL, so the guard is a constant.
	/// </summary>
	private bool TryVisitRootNullGuard(BinaryExpression node)
	{
		if (node.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
			return false;

		// ResolvesToNull reads the captured form too, and the parameter side is unwrapped,
		// since a hand-built tree converts the row to object to match the operand types.
		var parameter = node.Left.UnwrapConvertExpressions() is ParameterExpression left && ResolvesToNull(node.Right) ? left
			: node.Right.UnwrapConvertExpressions() is ParameterExpression right && ResolvesToNull(node.Left) ? right
			: null;

		if (parameter is null)
			return false;

		// Only the document row is known never to be null, and there the guard is a
		// constant. After a projection the parameter stands for the projected value,
		// which has no field name of its own to compare, so the shape is refused
		// rather than folded into a constant that would drop every row.
		if (!IsDocumentParameter(parameter))
		{
			throw new NotSupportedException(
				"A null comparison against a projected value is not supported: compare the "
				+ "document field instead, before the projection.");
		}

		_ = _builder.Append(node.NodeType == ExpressionType.Equal ? "false" : "true");
		return true;
	}

	/// <summary>
	/// Whether the parameter stands for the document row rather than a projected value.
	/// Matching the element type is not enough: a recursive type projects to itself, as in
	/// ".Select(n => n.Child)" over "Node.Child : Node?", and the projected value may well
	/// be null. Keep and Drop narrow the columns but leave the row, so the question is
	/// whether a Select has run, not which commands were emitted. The element type is a
	/// document type and is set before any Where runs, so it alone decides the first half.
	/// </summary>
	private bool IsDocumentParameter(ParameterExpression parameter) =>
		parameter.Type.IsAssignableFrom(_context.ElementType)
		&& !_context.HasProjected;

	private static string EscapeLikePattern(string value) =>
		// Pattern-level escaping only: a backslash escapes LIKE wildcards. String-literal
		// escaping (quotes, backslashes) is applied afterwards by EsqlFormatting.FormatString.
		value
			.Replace("\\", "\\\\")
			.Replace("*", "\\*")
			.Replace("?", "\\?");

	/// <summary>
	/// Rewrites <c>string.CompareOrdinal(a, b) &gt; 0</c>, or
	/// <c>string.Compare(a, b, StringComparison.Ordinal) &gt; 0</c>, into <c>a &gt; b</c>.
	/// Only comparisons against the constant zero carry an ordering, and only the
	/// explicitly ordinal forms are accepted: <c>CompareTo</c> and the two-argument
	/// <c>Compare</c> order by the current culture, which is not something ES|QL can be
	/// asked for, so they are refused with a pointer to the ordinal forms.
	/// <para>
	/// Even the ordinal forms are not identical to what Elasticsearch does: .NET compares
	/// UTF-16 code units, Elasticsearch the UTF-8 bytes of a keyword, and the two orders
	/// disagree on exactly one kind of pair, a supplementary character against a character
	/// in U+E000 to U+FFFF. A comparison is decided by the first character that differs,
	/// so when the value compared against holds neither a supplementary character nor
	/// one at or above U+E000, no such pair can arise, whatever the field holds, and the
	/// translation is exact. Only that case is translated; a value outside it, two fields
	/// compared with each other, the projected row, and a property whose converter writes
	/// the field in an order of its own, are refused rather than ordered wrongly.
	/// </para>
	/// </summary>
	private bool TryVisitStringComparison(BinaryExpression node)
	{
		if (node.NodeType is not (ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
			or ExpressionType.LessThan or ExpressionType.LessThanOrEqual))
			return false;

		var (call, zero, flipped) = node.Left is MethodCallExpression left
			? (left, node.Right, false)
			: node.Right is MethodCallExpression right ? (right, node.Left, true) : (null, null, false);

		if (call is null || !TryGetConstant(zero!, out var zeroValue) || zeroValue is not 0)
			return false;

		if (call.Method.DeclaringType != typeof(string)
			|| call.Method.Name is not ("CompareTo" or "Compare" or "CompareOrdinal"))
			return false;

		// From here the shape is the supported one, so anything refused is refused with
		// its own reason rather than the generic "only when compared to zero" message.
		var (first, second) = OrdinalOperands(call);
		EnsureOrderingIsExact(call.Method.Name, first, second);

		var op = node.NodeType switch
		{
			ExpressionType.GreaterThan => flipped ? "<" : ">",
			ExpressionType.GreaterThanOrEqual => flipped ? "<=" : ">=",
			ExpressionType.LessThan => flipped ? ">" : "<",
			_ => flipped ? ">=" : "<="
		};

		AppendOrderingComparison(call.Method.Name, first, second, op);
		return true;
	}

	/// <summary>
	/// The two operands of an ordinal comparison. Any other overload orders by the current
	/// culture, an ignore-case flag, a range or a non-string operand, and is refused.
	/// </summary>
	private static (Expression First, Expression Second) OrdinalOperands(MethodCallExpression call)
	{
		var parameters = call.Method.GetParameters();
		var ordinalForm = call.Object is null
			&& parameters.Length is 2 or 3
			&& parameters[0].ParameterType == typeof(string)
			&& parameters[1].ParameterType == typeof(string)
			&& (call.Method.Name == "CompareOrdinal"
				? parameters.Length == 2
				: parameters.Length == 3 && parameters[2].ParameterType == typeof(StringComparison));

		if (!ordinalForm)
		{
			throw new NotSupportedException(
				$"String method {call.Method.Name} is only supported as string.CompareOrdinal(a, b) or "
				+ "string.Compare(a, b, StringComparison.Ordinal): every other overload orders by the "
				+ "current culture, an ignore-case flag, a range or a non-string operand, none of "
				+ "which is the UTF-8 byte ordering ES|QL applies to a keyword field.");
		}

		if (parameters.Length == 3 && !IsOrdinalComparison(call.Arguments[2]))
		{
			throw new NotSupportedException(
				$"String method {call.Method.Name} with a StringComparison argument other than "
				+ "StringComparison.Ordinal is not supported: keyword values are ordered by their "
				+ "UTF-8 bytes and comparison is case-sensitive, so any other comparison mode asks "
				+ "for an ordering Elasticsearch does not apply.");
		}

		return (call.Arguments[0], call.Arguments[1]);
	}

	/// <summary>
	/// Whether the StringComparison argument asks for the ordering ES|QL performs.
	/// Only <see cref="StringComparison.Ordinal"/> does: keyword ordering is
	/// case-sensitive, so OrdinalIgnoreCase would order "a" and "B" the other way.
	/// <para>
	/// The instance methods carry their own check, on the path that visits a string method
	/// call. This one is for the static ordering forms, which are read here, inside the
	/// comparison against zero, and never reach that path.
	/// </para>
	/// </summary>
	private static bool IsOrdinalComparison(Expression expression) =>
		TryGetConstant(expression, out var mode) && mode is StringComparison.Ordinal;

	/// <summary>
	/// Whether the operand reads a field of the document rather than a value: the question
	/// is whether it depends on the lambda parameter, whatever its shape, since a function
	/// of a captured value is a value all the same.
	/// </summary>
	private static bool ReadsAField(Expression expression)
	{
		var finder = new ParameterFinder();
		_ = finder.Visit(expression);
		return finder.Found;
	}

	/// <summary>Whether the expression reads the element of an Any or All, anywhere in it.</summary>
	private static bool ReadsTheElement(Expression expression, ParameterExpression element)
	{
		var finder = new ParameterFinder(element);
		_ = finder.Visit(expression);
		return finder.Found;
	}

	/// <summary>Finds the lambda parameter anywhere in an expression, or the one given.</summary>
	private sealed class ParameterFinder(ParameterExpression? parameter = null) : ExpressionVisitor
	{
		public bool Found { get; private set; }

		protected override Expression VisitParameter(ParameterExpression node)
		{
			Found |= parameter is null || node == parameter;
			return base.VisitParameter(node);
		}
	}

	/// <summary>
	/// Refuses the operands whose ordering the translation cannot reproduce: the row
	/// itself, a null operand, two fields with no value to look at, and a value holding a
	/// character on which the UTF-16 and UTF-8 orderings can disagree.
	/// </summary>
	private void EnsureOrderingIsExact(string methodName, Expression first, Expression second)
	{
		// a projected scalar row has no field name of its own to compare, and emitting it
		// leaves the operand empty
		if (first.UnwrapConvertExpressions() is ParameterExpression || second.UnwrapConvertExpressions() is ParameterExpression)
		{
			throw new NotSupportedException(
				$"String method {methodName} against a projected value is not supported: compare "
				+ "the document field instead, before the projection.");
		}

		// .NET orders a non-null string above null, which a plain ES|QL comparison
		// against null does not reproduce; there is a field to test for null instead
		if (ResolvesToNull(first) || ResolvesToNull(second))
		{
			throw new NotSupportedException(
				$"String method {methodName} against null is not supported: compare the "
				+ "field with null directly, which ES|QL answers with IS NULL.");
		}

		// The UTF-16 and UTF-8 orders disagree only between a supplementary character
		// and one in U+E000 to U+FFFF. With a value holding neither, the first differing
		// character can never be such a pair, so the translation is exact for any field.
		var value = TryGetConstant(first, out var firstValue) ? firstValue
			: TryGetConstant(second, out var secondValue) ? secondValue
			: null;

		if (value is not string text)
		{
			// a side that reads no field is a value all the same, even where the resolver
			// cannot fold it, so it is refused for what it is rather than as a field
			var valueSide = !ReadsAField(first) || !ReadsAField(second);

			throw new NotSupportedException(valueSide
				? $"String method {methodName} against a value the translation cannot read is not "
					+ "supported: without the value it cannot tell whether the UTF-16 ordering of .NET "
					+ "and the UTF-8 ordering of Elasticsearch agree on the comparison. Compute the "
					+ "value before the query and compare against the result."
				: $"String method {methodName} between two fields is not supported: without a "
					+ "value to look at, the translation cannot tell whether the UTF-16 ordering of .NET "
					+ "and the UTF-8 ordering of Elasticsearch agree on the comparison.");
		}

		if (text.Any(character => char.IsSurrogate(character) || character >= '\uE000'))
		{
			throw new NotSupportedException(
				$"String method {methodName} against a value holding a character at or above "
				+ "U+E000, or outside the Basic Multilingual Plane, is not supported: on such a value "
				+ "the UTF-16 ordering of .NET and the UTF-8 ordering of Elasticsearch can disagree, "
				+ "so the comparison is left untranslated rather than answered with the wrong order.");
		}

		// The field holds what the converter writes, and the value is emitted through it
		// too, so the order Elasticsearch applies is the order of the written forms. A
		// converter is free not to preserve the order of the values it is given, and the
		// check above reads the value as written in the source, so neither it nor that
		// order carries over to what the field actually holds.
		if (_context.Metadata.FindPropertyConverter(ConvertedMember(first) ?? ConvertedMember(second)) is not null)
		{
			throw new NotSupportedException(
				$"String method {methodName} on a property with a JsonConverter is not supported: "
				+ "the field holds what the converter writes, which need not be ordered the way the "
				+ "values it is given are, so the ordering of the two sides cannot be reproduced.");
		}
	}

	/// <summary>
	/// The member an operand reads, looked for through the calls wrapped around it: a
	/// MultiField or a scalar function still reads the field the converter writes, so the
	/// ordering of the written forms is the one that decides the comparison.
	/// </summary>
	private static MemberInfo? ConvertedMember(Expression expression)
	{
		if (EntityPropertyMember(expression) is { } direct)
			return direct;

		return expression.UnwrapConvertExpressions() is MethodCallExpression call
			? new[] { call.Object }.Concat(call.Arguments).FirstOrDefault(a => a is not null && ConvertedMember(a) is not null) is { } found
				? ConvertedMember(found)
				: null
			: null;
	}

	/// <summary>
	/// Emits the comparison with the ordering of a missing operand spelled out: .NET
	/// orders null before every string, where a comparison against a missing field is
	/// null in ES|QL and drops the row. One side is a value by now, so at most one side
	/// can be missing, and its ordering can be spelled out for the field itself, not for
	/// an expression of it, whose value for a missing field is not the field's null.
	/// </summary>
	private void AppendOrderingComparison(string methodName, Expression first, Expression second, string op)
	{
		var firstMayBeMissing = AsNullableField(first);
		var secondMayBeMissing = AsNullableField(second);

		if ((firstMayBeMissing is null && ReadsANullableField(first)) || (secondMayBeMissing is null && ReadsANullableField(second)))
		{
			throw new NotSupportedException(
				$"String method {methodName} over an expression of a field that can be missing is "
				+ "not supported: the ordering of a missing value can be spelled out for the field "
				+ "itself, not for an expression of it. Compare the field directly.");
		}

		var descending = op[0] == '>';
		var guardedField = firstMayBeMissing ?? secondMayBeMissing;
		var guardClause = firstMayBeMissing is not null
			// a missing left operand sorts first: below anything, never above
			? descending ? " IS NOT NULL AND " : " IS NULL OR "
			// a missing right operand sorts first: anything is above it, nothing below
			: descending ? " IS NULL OR " : " IS NOT NULL AND ";

		if (guardedField is not null)
			_ = _builder.Append('(').Append(guardedField).Append(guardClause);

		// as for a relational operator: the value is serialized through the converter of the property it is compared with
		_comparisonPropertyContext = EntityPropertyMember(first) ?? EntityPropertyMember(second);
		_ = Visit(first);
		_ = _builder.Append(' ').Append(op).Append(' ');
		_ = Visit(second);
		_comparisonPropertyContext = null;

		if (guardedField is not null)
			_ = _builder.Append(')');
	}

	/// <summary>
	/// The emitted path of an operand that can be missing: a member path declared nullable
	/// at any step, or a multi-field of one. A missing parent leaves the whole path null,
	/// so the guard goes on the path as emitted, whatever the last member says.
	/// </summary>
	private string? AsNullableField(Expression expression) => expression switch
	{
		MemberExpression member when IsNullableFieldPath(member) => ResolveFieldPath(member),
		MethodCallExpression { Method.Name: "MultiField", Arguments: [MemberExpression member, ConstantExpression { Value: string }] } call
			when call.Method.DeclaringType == typeof(GeneralPurposeExtensions) && IsNullableFieldPath(member)
			=> call.ResolveFieldName(_context.Metadata),
		_ => null
	};

	/// <summary>
	/// Whether the member path is rooted in the parameter and can be missing: "l.Host.Name"
	/// is missing whenever Host is, so any nullable member along the path counts.
	/// </summary>
	private static bool IsNullableFieldPath(MemberExpression member)
	{
		if (!ExpressionTranslationHelpers.IsRootedInParameter(member))
			return false;

		// the chain is walked through the conversions a cast leaves on it, as
		// IsRootedInParameter and ResolveMemberFieldPath do
		for (Expression? current = member; current is MemberExpression step; current = step.Expression?.UnwrapConvertExpressions())
		{
			if (CanBeMissing(step.Member))
				return true;
		}

		return false;
	}

	/// <summary>Whether any member path read anywhere in the expression can be missing.</summary>
	private static bool ReadsANullableField(Expression expression)
	{
		var finder = new NullableFieldFinder();
		_ = finder.Visit(expression);
		return finder.Found;
	}

	private sealed class NullableFieldFinder : ExpressionVisitor
	{
		public bool Found { get; private set; }

		protected override Expression VisitMember(MemberExpression node)
		{
			if (IsNullableFieldPath(node))
				Found = true;

			return base.VisitMember(node);
		}
	}

	/// <summary>
	/// The nullability the compiler recorded for a member or a parameter: 2 for annotated
	/// as nullable, 1 for annotated as not, 0 or null for oblivious, which a consumer
	/// building without nullable reference types leaves everywhere.
	/// <para>
	/// The annotation sits on the member itself, or on a declaring type as a context when
	/// every member shares it. Both are read from the attribute data rather than by
	/// instantiating the attribute, which keeps the check out of the trimmer's way. A
	/// <c>Nullable&lt;T&gt;</c> is nullable whatever the annotations say, and any other
	/// value type is not.
	/// </para>
	/// </summary>
	private static byte? NullabilityOf(MemberInfo member)
	{
		var type = member switch
		{
			PropertyInfo property => property.PropertyType,
			FieldInfo field => field.FieldType,
			_ => null
		};

		if (type is null)
			return null;

		if (Nullable.GetUnderlyingType(type) is not null)
			return 2;

		if (type.IsValueType)
			return 1;

		return NullableFlag(member.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableAttribute")
			?? ContextNullability(member.DeclaringType);
	}

	/// <summary>The same for a constructor parameter, which stands for the member it initializes.</summary>
	private static byte? NullabilityOf(ParameterInfo parameter)
	{
		if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
			return 2;

		if (parameter.ParameterType.IsValueType)
			return 1;

		return NullableFlag(parameter.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableAttribute")
			?? NullableFlag(parameter.Member.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableContextAttribute")
			?? ContextNullability(parameter.Member.DeclaringType);
	}

	/// <summary>The nullable context a declaring type carries, walking out to its own declaring types.</summary>
	private static byte? ContextNullability(Type? declaringType)
	{
		for (var declaring = declaringType; declaring is not null; declaring = declaring.DeclaringType)
		{
			var context = NullableFlag(declaring.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableContextAttribute");

			if (context is not null)
				return context;
		}

		return null;
	}

	// The answer for a member never changes, and reading it walks the member's attribute
	// data and its declaring types.
	private static readonly ConcurrentDictionary<MemberInfo, byte?> NullabilityByMember = new();

	/// <summary>
	/// Whether a guard belongs on the member: everything except one the compiler states is
	/// never null. A guard on a column that is never null is a no-op, while a missing one
	/// changes the rows, so an unannotated member, as an anonymous type's is, is guarded.
	/// </summary>
	private static bool CanBeMissing(MemberInfo member) => IsDeclaredNullable(member);

	/// <summary>
	/// Whether the member can hold the null a dropped guard produces: only an explicit
	/// non-nullable annotation says it cannot. An oblivious member, which a consumer
	/// building with nullable reference types disabled has everywhere, can.
	/// </summary>
	internal static bool IsDeclaredNullable(MemberInfo member) =>
		NullabilityByMember.GetOrAdd(member, NullabilityOf) != 1;

	/// <summary>The same for a constructor parameter.</summary>
	internal static bool IsDeclaredNullable(ParameterInfo parameter) => NullabilityOf(parameter) != 1;

	/// <summary>
	/// The first nullability flag carried by the named attribute: 2 for annotated
	/// (nullable), 1 for not annotated, 0 for oblivious. The constructor takes either one
	/// byte or an array whose first element describes the outermost type.
	/// </summary>
	private static byte? NullableFlag(IEnumerable<CustomAttributeData> attributes, string attributeName)
	{
		var data = attributes.FirstOrDefault(attribute => attribute.AttributeType.FullName == attributeName);

		if (data is null || data.ConstructorArguments.Count == 0)
			return null;

		return data.ConstructorArguments[0].Value switch
		{
			byte flag => flag,
			IReadOnlyCollection<CustomAttributeTypedArgument> { Count: > 0 } flags => flags.First().Value as byte?,
			_ => null
		};
	}

	/// <summary>A constant the expression evaluates to, when it has one that is not null.</summary>
	private static bool TryGetConstant(Expression expression, out object? value)
	{
		try
		{
			value = GetConstantValue(expression);
			return value is not null;
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or TargetInvocationException)
		{
			// the same filter the resolver's own callers use: a value that cannot be read
			// is not a constant, anything else is a real bug and propagates
			value = null;
			return false;
		}
	}

	/// <summary>
	/// Predicates over a multi-value document field: <c>field.Any(...)</c>,
	/// <c>field.All(...)</c> and <c>field.Contains(value)</c>. A document holds every
	/// value of the field at once, so the quantifier is answered on the field itself,
	/// without the row duplication MV_EXPAND would introduce.
	/// <para>
	/// Equality here is the store's: the field is compared the way Elasticsearch compares
	/// its values. The property's collection type is how a document is materialized, and
	/// a comparer set on an instance is not visible when the query is translated, just as
	/// a scalar comparison on a string property does not see one either. MATCH compares
	/// the way the field is indexed: exactly on a keyword field, through the analyzer on
	/// a text field, as MATCH always does; a keyword multi-field of a collection is not a
	/// shape translated here.
	/// </para>
	/// </summary>
	private bool TryVisitMultiValueField(MethodCallExpression node)
	{
		var source = TryGetMultiValueSource(node);
		if (source is null)
			return false;

		ThrowIfNotAField(node.Method.Name, source);
		ThrowIfTheValuesCannotBeCompared(node.Method.Name, source);
		var name = ResolveMultiValueField(source);

		// field.Any() with no predicate: the field simply has to hold a value
		if (node.Method.Name == "Any" && node.Arguments.Count == (node.Method.IsStatic ? 1 : 0))
		{
			// LINQ reads a missing field as an empty sequence, where Any() is false; an empty
			// array is stored as a missing field, so IS NOT NULL is the same test, and one
			// Lucene answers as an exists query
			_ = _builder.Append(name).Append(" IS NOT NULL");
			return true;
		}

		return node.Method.Name == "Contains"
			? TryVisitFieldContains(node, source, name)
			: TryVisitQuantifier(node, name);
	}

	/// <summary>
	/// A predicate over a multi-value field compared with a boolean, <c>p.Tags.Any() == false</c>.
	/// Elasticsearch takes neither MATCH nor a bare IS NOT NULL as an operand of a comparison, so
	/// a boolean known when the query is translated picks the predicate or its negation, and one
	/// known only when the query runs is refused.
	/// </summary>
	private bool TryVisitMultiValueComparison(BinaryExpression node)
	{
		if (node.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
			return false;

		var left = node.Left.UnwrapConvertExpressions();
		var right = node.Right.UnwrapConvertExpressions();
		var predicate = left is MethodCallExpression leftCall && TryGetMultiValueSource(leftCall) is not null ? leftCall
			: right is MethodCallExpression rightCall && TryGetMultiValueSource(rightCall) is not null ? rightCall
			: null;

		if (predicate is null)
			return false;

		if (!TryGetConstant(predicate == left ? right : left, out var value) || value is not bool flag)
		{
			throw new NotSupportedException(
				$"Comparing {predicate.Method.Name} over {ResolveMultiValueField(TryGetMultiValueSource(predicate)!)} with a boolean "
				+ "known only when the query runs is not supported: Elasticsearch takes neither MATCH nor IS NOT NULL as an "
				+ "operand of a comparison. Compare with true or false, or write the predicate, negated with ! where needed.");
		}

		// "p.Tags.Any() == false" is "!p.Tags.Any()"
		_ = Visit(flag == (node.NodeType == ExpressionType.Equal) ? predicate : Expression.Not(predicate));
		return true;
	}

	/// <summary>
	/// The field an Any, All or Contains of the framework's own is called on, or null when the
	/// call is none of those or its source is not a field of the document.
	/// </summary>
	private static Expression? TryGetMultiValueSource(MethodCallExpression node)
	{
		if (node.Method.Name is not ("All" or "Any" or "Contains"))
			return null;

		// the source must be a document field, not a constant collection
		var source = node.Method.IsStatic
			? node.Arguments.Count > 0 ? node.Arguments[0] : null
			: node.Object;

		// arrays reach us through MemoryExtensions.Contains(ReadOnlySpan<T>, T)
		if (source is not null && node.Method.DeclaringType == typeof(MemoryExtensions))
			source = TryUnwrapMemoryExtensionsSource(source);

		// only the framework's own Any, All and Contains: a method of that name defined
		// elsewhere may mean anything, and is left to fail soft as before
		return source is not null && IsMultiValueField(source) && IsFrameworkMethod(node.Method)
			? source
			: null;
	}

	/// <summary>
	/// Refuses a source that reads a field without being one: a LINQ operator over its values,
	/// such as <c>p.Tags.Where(t => t != "")</c>, or a row that a Select made the collection itself.
	/// </summary>
	private static void ThrowIfNotAField(string methodName, Expression source)
	{
		switch (source.UnwrapConvertExpressions())
		{
			case MemberExpression:
				return;

			case MethodCallExpression call when call.Method.Name == "MultiField" && call.Method.DeclaringType == typeof(GeneralPurposeExtensions):
				return;

			case ParameterExpression:
				throw new NotSupportedException(
					$"{methodName} over a projected row is not supported: the row is the collection itself, with no field "
					+ "name to test. Project the collection into a member, as in Select(p => new { p.Tags }).");

			default:
				throw new NotSupportedException(
					$"{methodName} over {source} is not supported: a field is tested as a whole, as it is stored, and a "
					+ "LINQ operator over its values would need them read one at a time.");
		}
	}

	/// <summary>
	/// Refuses a field whose values the translation cannot compare with a value: a collection
	/// of objects, and a collection written through a JsonConverter.
	/// </summary>
	private void ThrowIfTheValuesCannotBeCompared(string methodName, Expression source)
	{
		// A collection of objects is an object in the mapping: ES|QL has a column for each
		// of its fields and none for the objects themselves, so there is nothing to count
		// or to compare a value with.
		if (IsWrittenAsObject(ElementType(source.Type)))
		{
			throw new NotSupportedException(
				$"{methodName} over a collection of objects is not supported: ES|QL has a column for "
				+ "each field of the objects and none for the objects themselves.");
		}

		// The field holds what the converter writes, and the values compared are emitted as
		// given: a converter of the collection, on the property, on its type or among the
		// serializer's converters, does not apply to one of its values, so the two need not
		// meet, and the count of values need not be the one written either.
		if (_context.Metadata.FindPropertyConverter(EntityPropertyMember(source)) is not null
			|| _context.HasRegisteredConverter(source.Type)
			|| source.Type.IsDefined(typeof(JsonConverterAttribute), inherit: false))
		{
			throw new NotSupportedException(
				$"{methodName} over a collection written through a JsonConverter is not supported: the "
				+ "field holds what the converter writes, which need not be one value per element, so neither "
				+ "the values compared nor whether the field holds any follow from the collection.");
		}
	}

	/// <summary>
	/// <c>field.Contains(value)</c>, which is <c>field.Any(x =&gt; x == value)</c> and is
	/// answered as that, and only that overload: one taking a comparer asks for a comparison
	/// the translation cannot honour.
	/// </summary>
	private bool TryVisitFieldContains(MethodCallExpression node, Expression source, string name)
	{
		if (TakesAnEqualityComparer(node))
			throw ContainsWithAnEqualityComparer();

		if (node.Arguments.Count != (node.Method.IsStatic ? 2 : 1))
			return false;

		var compared = GetComparedValue(node.Arguments[^1], ElementType(source.Type), name);

		return TryAppendQuantified(name, all: false, new ElementPredicate(ElementPredicateKind.Equal, [compared], Negated: false));
	}

	/// <summary><c>field.Any(predicate)</c> and <c>field.All(predicate)</c>, over one predicate on the element.</summary>
	private bool TryVisitQuantifier(MethodCallExpression node, string name)
	{
		if (StripQuotes(node.Arguments[^1]) is not LambdaExpression { Parameters.Count: 1 } lambda)
			return false;

		var predicate = TryParseElementPredicate(lambda.Body, lambda.Parameters[0], name, negated: false);

		return predicate is not null
			&& TryAppendQuantified(name, all: node.Method.Name == "All", predicate.Value);
	}

	/// <summary>
	/// Reads the body of the lambda passed to Any/All as one predicate on the element.
	/// Null guards on the element are dropped, since a stored value is never null.
	/// </summary>
	private ElementPredicate? TryParseElementPredicate(Expression body, ParameterExpression element, string field, bool negated) =>
		body switch
		{
			UnaryExpression { NodeType: ExpressionType.Not } negation =>
				TryParseElementPredicate(negation.Operand, element, field, negated: !negated),

			// "x != null && P(x)" is P(x)
			BinaryExpression { NodeType: ExpressionType.AndAlso } conjunction when IsNullGuard(conjunction.Left, element, ExpressionType.NotEqual) =>
				TryParseElementPredicate(conjunction.Right, element, field, negated: negated),

			BinaryExpression { NodeType: ExpressionType.AndAlso } conjunction when IsNullGuard(conjunction.Right, element, ExpressionType.NotEqual) =>
				TryParseElementPredicate(conjunction.Left, element, field, negated: negated),

			// "x == null || P(x)" is P(x)
			BinaryExpression { NodeType: ExpressionType.OrElse } disjunction when IsNullGuard(disjunction.Left, element, ExpressionType.Equal) =>
				TryParseElementPredicate(disjunction.Right, element, field, negated: negated),

			// Any(a || b) is Any(a) || Any(b), which the caller can write
			BinaryExpression { NodeType: ExpressionType.OrElse } => throw OrInsideThePredicate(field),

			BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual } comparison =>
				TryParseElementEquality(comparison, element, field, negated: negated),

			BinaryExpression
			{
				NodeType: ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
					or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
			} ordering => TryParseElementOrdering(ordering, element, field, negated: negated),

			MethodCallExpression call => TryParseElementCall(call, element, field, negated: negated),

			_ => null
		};

	/// <summary>
	/// <c>x == v</c> or <c>x != v</c>, with the element on either side. C# compares an enum, a
	/// short or a byte as an int, and an int with a double as a double, so the element may sit
	/// inside a conversion.
	/// </summary>
	private ElementPredicate? TryParseElementEquality(BinaryExpression comparison, ParameterExpression element, string field, bool negated)
	{
		var value = TryGetComparand(comparison, element, field, out _);
		if (value is null)
			return null;

		var compared = GetComparedValue(value, element.Type, field);
		var isEqual = comparison.NodeType == ExpressionType.Equal;
		return new ElementPredicate(ElementPredicateKind.Equal, [compared], Negated: isEqual ? negated : !negated);
	}

	/// <summary>
	/// <c>x &gt; v</c> and the other orderings, with the element on either side, inside a
	/// conversion as for equality.
	/// </summary>
	private ElementPredicate? TryParseElementOrdering(BinaryExpression ordering, ParameterExpression element, string field, bool negated)
	{
		// "10 < x" is "x > 10": keep the element on the left
		var value = TryGetComparand(ordering, element, field, out var elementOnLeft);
		if (value is null)
			return null;

		var compared = GetComparedValue(value, element.Type, field);

		var kind = (ordering.NodeType, elementOnLeft) switch
		{
			(ExpressionType.GreaterThan, true) or (ExpressionType.LessThan, false) => ElementPredicateKind.GreaterThan,
			(ExpressionType.GreaterThanOrEqual, true) or (ExpressionType.LessThanOrEqual, false) => ElementPredicateKind.GreaterThanOrEqual,
			(ExpressionType.LessThan, true) or (ExpressionType.GreaterThan, false) => ElementPredicateKind.LessThan,
			_ => ElementPredicateKind.LessThanOrEqual
		};

		return new ElementPredicate(kind, [compared], Negated: negated);
	}

	/// <summary>
	/// The side of a comparison the element is compared with, the element sitting on the other
	/// side. A comparison that reads the element through a function, such as
	/// <c>t.Length &gt; 3</c>, holds for one value at a time and is refused.
	/// </summary>
	private static Expression? TryGetComparand(BinaryExpression comparison, ParameterExpression element, string field, out bool elementOnLeft)
	{
		elementOnLeft = comparison.Left.UnwrapConvertExpressions() == element;

		var value = elementOnLeft ? comparison.Right
			: comparison.Right.UnwrapConvertExpressions() == element ? comparison.Left
			: null;

		if (ReadsTheElement(value ?? comparison, element))
			throw PredicateOverIndividualValues(field);

		return value;
	}

	/// <summary>
	/// The value an element is compared with, as the expression to render, which is rendered
	/// as a scalar comparison renders it, a date computed from DateTime.UtcNow included. A
	/// value that reads another field is refused, and so is null, which a stored value never is.
	/// </summary>
	private Expression GetComparedValue(Expression value, Type elementType, string field)
	{
		if (ReadsAField(value))
			throw ComparisonWithAnotherField(field);

		if (ResolvesToNull(value))
			throw ComparisonWithNull(field);

		var enumType = Nullable.GetUnderlyingType(elementType) ?? elementType;
		if (!enumType.IsEnum)
			return value;

		// "x == Priority.High" reaches the tree as "(int)x == 2": the number is turned back into
		// the enum, as a scalar comparison does, so that an enum written by name is compared by
		// name. A number read only when the query runs stays as it is, as in a scalar comparison.
		var unwrapped = value.UnwrapConvertExpressions();
		if ((Nullable.GetUnderlyingType(unwrapped.Type) ?? unwrapped.Type) == enumType)
			return unwrapped;

		// a captured number cast to the enum keeps its name as a parameter, which holds the enum;
		// the value the null check resolved is reused rather than read a second time
		if (_resolvedCaptures.TryGetValue(unwrapped, out var captured) && captured is not null)
		{
			_resolvedCaptures[unwrapped] = Enum.ToObject(enumType, captured);
			return unwrapped;
		}

		return TryGetConstant(unwrapped, out var number) && number is not null
			? Expression.Constant(Enum.ToObject(enumType, number), enumType)
			: value;
	}

	private static NotSupportedException ComparisonWithAnotherField(string field) => new(
		$"Comparing the values of {field} with another field is not supported: they are compared with a "
		+ "value the query carries, such as a literal or a captured variable.");

	private static NotSupportedException ComparisonWithNull(string field) => new(
		$"Comparing the values of {field} with null is not supported: Elasticsearch stores no null among "
		+ "the values of a field, and MATCH does not take one.");

	private static NotSupportedException OrInsideThePredicate(string field) => new(
		$"An OR inside the predicate over {field} is not supported: write Any(v => a || b) as "
		+ "Any(v => a) || Any(v => b), and an OR of equalities as membership, Any(v => values.Contains(v)).");

	private static NotSupportedException PredicateOverIndividualValues(string field) => new(
		$"A predicate over the individual values of {field} is not supported: MATCH, MV_MIN, "
		+ "MV_MAX and MV_COUNT answer a test over the field as a whole, and a test such as "
		+ "StartsWith holds for one value at a time. Compare the values with equality, or "
		+ "test the field with one of the supported comparisons.");

	/// <summary>
	/// A call on the element, <c>x.StartsWith("a")</c> and the like, or membership of the
	/// element in a constant collection, <c>values.Contains(x)</c>.
	/// </summary>
	private static ElementPredicate? TryParseElementCall(MethodCallExpression call, ParameterExpression element, string field, bool negated)
	{
		// x.StartsWith("a"), x.EndsWith("a"), x.Contains("a"), with or without a
		// StringComparison and whatever the value: either way the test holds for one value at a time
		if (call.Object == element
			&& call.Method.DeclaringType == typeof(string)
			&& (call.Arguments.Count == 1
				|| (call.Arguments.Count == 2 && call.Arguments[1].Type == typeof(StringComparison))))
		{
			if (!TryGetTextPredicateKind(call.Method.Name, out var kind))
				return null;

			return new ElementPredicate(kind, [call.Arguments[0]], Negated: negated);
		}

		// values.Contains(x), over a constant collection: another method taking one value and
		// returning a bool, such as Remove, is no membership test. The element may sit inside a
		// conversion, as for equality: over a short, wanted.Contains(s) is wanted.Contains((int)s)
		if (call.Method.Name != "Contains"
			|| !TryGetContainsArguments(call, out var valueExpression, out var collection)
			|| valueExpression.UnwrapConvertExpressions() != element
			|| collection is null)
			return null;

		// enumerating the collection loses the equality it was built with: a set
		// holding "IOT" under an ordinal-ignore-case comparer contains "iot",
		// which the emitted comparison does not reproduce
		if (!UsesDefaultEquality(collection))
		{
			throw new NotSupportedException(
				$"Contains over a {TypeName(collection.GetType())} is not supported: a set, a dictionary "
				+ "or a collection type of your own may compare its values in a way of its own, "
				+ "which the emitted comparison would not follow. Pass an array, a List or a "
				+ "LINQ query, which compare with default equality.");
		}

		var candidates = collection.Cast<object?>().ToList();

		// a stored value is never null, and MATCH(field, null) is not valid ES|QL
		if (candidates.Any(candidate => candidate is null))
			throw ComparisonWithNull(field);

		// numbers compared with an enum are turned back into it, as for equality, so that an
		// enum written by name is compared by name
		var enumType = Nullable.GetUnderlyingType(element.Type) ?? element.Type;
		var values = candidates.Cast<object>();
		if (enumType.IsEnum)
			values = values.Select(value => value.GetType() == enumType ? value : Enum.ToObject(enumType, value));

		return new ElementPredicate(ElementPredicateKind.In, [.. values.Select(Expression.Constant)], Negated: negated);
	}

	/// <summary>
	/// Any(P) and All(P) over the values of a field. A negated predicate is pushed into
	/// the quantifier, since Any(not P) is "not All(P)" and All(not P) is "not Any(P)".
	/// </summary>
	private bool TryAppendQuantified(string name, bool all, ElementPredicate predicate)
	{
		// "Any(not P)" is "not All(P)" and "All(not P)" is "not Any(P)": the negation
		// moves onto the quantifier, which flips
		if (predicate.Negated)
		{
			_ = _builder.Append("NOT ");
			all = !all;
		}

		switch (predicate.Kind)
		{
			case ElementPredicateKind.Equal:
				AppendEquality(name, all, predicate);
				return true;

			case ElementPredicateKind.In:
				AppendMembership(name, all, predicate);
				return true;

			case ElementPredicateKind.GreaterThan or ElementPredicateKind.GreaterThanOrEqual
				or ElementPredicateKind.LessThan or ElementPredicateKind.LessThanOrEqual:
				AppendOrdering(name, all, predicate);
				return true;

			// StartsWith and the rest test one value at a time, which needs the field read
			// position by position: the shape is refused rather than answered by a test that
			// reads the whole field.
			default:
				throw PredicateOverIndividualValues(name);
		}
	}

	/// <summary>Any and All over equality with one value.</summary>
	private void AppendEquality(string name, bool all, ElementPredicate predicate)
	{
		// a document matches MATCH when any of the field's values does
		if (!all)
		{
			AppendPresentMatches(name, [TranslateSubExpression(predicate.Values[0])]);
			return;
		}

		// every value equals v: the field holds one distinct value, and it matches.
		// A missing field has no value that differs, as All() over an empty sequence is true.
		_ = _builder.Append('(').Append(name).Append(" IS NULL OR (MV_COUNT(MV_DEDUPE(").Append(name).Append(")) == 1 AND ");
		AppendMatch(name, TranslateSubExpression(predicate.Values[0]));
		_ = _builder.Append("))");
	}

	/// <summary>Any over membership in a captured collection, with a MATCH for each of its values.</summary>
	private void AppendMembership(string name, bool all, ElementPredicate predicate)
	{
		// "every value is one of these", which "some value is not" negates, has no answer
		// over the field as a whole: MATCH answers whether some value is
		if (all)
		{
			throw new NotSupportedException(
				$"A membership test that every value of {name} must pass, or that some value must fail, is not "
				+ "supported: MATCH answers whether some value is one of the given values, not whether every "
				+ "value is. Test with Any, and negate the Any itself to ask that no value is one of them.");
		}

		if (predicate.Values.Count == 0)
		{
			_ = _builder.Append("false");
			return;
		}

		// each value adds one level to the expression
		if (predicate.Values.Count > MaxMatchedValues)
		{
			throw new NotSupportedException(
				$"A collection of {predicate.Values.Count} values is not supported here: each value adds "
				+ $"a level to the expression Elasticsearch parses, and at most {MaxMatchedValues} fit.");
		}

		AppendPresentMatches(name, [.. predicate.Values.Select(TranslateSubExpression)]);
	}

	/// <summary>
	/// Any and All over an ordering: some value is above v when the largest is, and every
	/// value is when the smallest is.
	/// </summary>
	private void AppendOrdering(string name, bool all, ElementPredicate predicate)
	{
		var upper = predicate.Kind is ElementPredicateKind.GreaterThan or ElementPredicateKind.GreaterThanOrEqual;
		var aggregate = all == upper ? "MV_MIN" : "MV_MAX";
		var op = predicate.Kind switch
		{
			ElementPredicateKind.GreaterThan => ">",
			ElementPredicateKind.GreaterThanOrEqual => ">=",
			ElementPredicateKind.LessThan => "<",
			_ => "<="
		};

		// MV_MIN and MV_MAX are null over a missing field, and so would be the
		// whole predicate, which then answers neither true nor false. A missing
		// field is an empty sequence: All holds over it and Any does not, and
		// saying so explicitly keeps an enclosing NOT meaningful.
		_ = _builder.Append('(').Append(name).Append(all ? " IS NULL OR " : " IS NOT NULL AND ");

		_ = _builder.Append(aggregate).Append('(').Append(name).Append(") ").Append(op).Append(' ')
			.Append(TranslateSubExpression(predicate.Values[0])).Append(')');
	}

	/// <summary>
	/// Whether the method is the framework's own: Enumerable, MemoryExtensions and, for an
	/// ImmutableArray, ImmutableArrayExtensions for the static forms, and the collections of
	/// the base library for the instance ones. These are recognised by namespace: their
	/// assemblies differ between frameworks, HashSet being in System.Core and LinkedList,
	/// Queue and Stack in System on .NET Framework.
	/// </summary>
	private static bool IsFrameworkMethod(MethodInfo method)
	{
		var declaring = method.DeclaringType;

		if (declaring is null)
			return false;

		if (method.IsStatic)
		{
			return declaring == typeof(Enumerable)
				|| declaring == typeof(MemoryExtensions)
				// by name: the netstandard2.0 build does not reference System.Collections.Immutable
				|| declaring.FullName == "System.Linq.ImmutableArrayExtensions";
		}

		return declaring.Namespace is "System.Collections.Concurrent"
			or "System.Collections.Frozen"
			or "System.Collections.Generic"
			or "System.Collections.Immutable"
			or "System.Collections.ObjectModel";
	}

	/// <summary>
	/// The column a field expression names, with the compiler's transparent-identifier
	/// prefixes stripped the way ordinary field predicates do.
	/// </summary>
	private string ResolveMultiValueField(Expression expression) =>
		expression is MemberExpression member
			? ResolveFieldPath(member)
			: expression.ResolveFieldName(_context.Metadata);

	/// <summary>
	/// A field that holds more than one value: a collection of the document, which is
	/// what <see cref="TypeHelper.IsEnumerableType"/> counts as one. A dictionary is an
	/// object in the mapping rather than a list of values, and is not.
	/// </summary>
	private static bool IsMultiValueField(Expression expression) =>
		ReadsAField(expression) && TypeHelper.IsEnumerableType(expression.Type);

	private static Type ElementType(Type collectionType) =>
		TypeHelper.FindGenericType(typeof(IEnumerable<>), collectionType)!.GetGenericArguments()[0];

	/// <summary>
	/// Whether the serializer writes the values as objects, for which ES|QL has a column for
	/// each of their fields and none for the values themselves. The type alone does not say:
	/// a Uri is a class, and the serializer writes it as a string.
	/// </summary>
	private bool IsWrittenAsObject(Type elementType)
	{
		try
		{
			return _context.Metadata.Options.GetTypeInfo(elementType).Kind is JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary;
		}
		catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
		{
			// a type the serializer has no contract for is judged by its shape
			return ExpressionTranslationHelpers.IsObjectSelectionType(elementType);
		}
	}

	private static string TypeName(Type type) =>
		type.Name.IndexOf('`') is var arity and >= 0 ? type.Name.Substring(0, arity) : type.Name;

	// MATCH is an analyzed search on a text-mapped field, so it matches more than equality
	// does. MV_CONTAINS (preview since 9.2) and MV_INTERSECTS (preview since 9.4) are the
	// exact primitives, and replace this once they are generally available.
	private void AppendMatch(string field, string renderedValue)
	{
		ThrowIfMatchFollowsLimitStatsOrFork();
		_ = _builder.Append("MATCH(").Append(field).Append(", ").Append(renderedValue).Append(')');
	}

	// Elasticsearch rejects MATCH after LIMIT, STATS and FORK, and would only say so when the
	// query runs. MV_CONTAINS for equality and Contains, and MV_INTERSECTS for membership in a
	// captured collection, lift this: both are evaluated per row rather than through the index,
	// so the position rule does not apply to them, and nothing is pushed to the index after
	// those commands anyway. Both are still preview (9.2 and 9.4): the refusal stays until they
	// are generally available, and this is the place to revisit then.
	private void ThrowIfMatchFollowsLimitStatsOrFork()
	{
		if (_matchPositionChecked)
			return;

		_matchPositionChecked = true;
		var command = FindCommandBlockingMatch(_context.Commands) ?? _context.ParentCommandBlockingMatch;

		if (command is not null)
		{
			throw new NotSupportedException(
				$"A predicate on a multi-value field is not supported after {command}: it translates to MATCH, "
				+ $"which Elasticsearch does not allow after {command}.");
		}
	}

	/// <summary>
	/// The first LIMIT, STATS or FORK among the commands, which Elasticsearch does not allow MATCH
	/// after. A raw fragment is text, and its first word says which command it is.
	/// </summary>
	internal static string? FindCommandBlockingMatch(IEnumerable<QueryCommand> commands) =>
		commands
			.Select(command => command switch
			{
				LimitCommand => "LIMIT",
				StatsCommand => "STATS",
				ForkCommand => "FORK",
				RawFragmentCommand raw => FindKeywordBlockingMatch(raw.Fragment),
				_ => null
			})
			.FirstOrDefault(command => command is not null);

	// ES|QL keywords are case-insensitive and any whitespace may follow them, as ForkBranchVisitor
	// reads a raw LIMIT
	private static string? FindKeywordBlockingMatch(string fragment)
	{
		var trimmed = fragment.TrimStart();
		var keywords = new[] { "LIMIT", "STATS", "FORK" };

		return Array.Find(keywords, keyword =>
			trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
			&& (trimmed.Length == keyword.Length || char.IsWhiteSpace(trimmed[keyword.Length])));
	}

	/// <summary>
	/// MATCH over each value, any of them matching, with a document that has no values
	/// answered false. A shard whose index does not map the field has it replaced by null,
	/// and MATCH over null is null (elastic/elasticsearch#137430), which an enclosing NOT
	/// would keep null and so drop the document. Any over an empty sequence is false, and
	/// it is stated, as for MV_MIN and MV_MAX.
	/// </summary>
	private void AppendPresentMatches(string field, IReadOnlyList<string> renderedValues)
	{
		_ = _builder.Append('(').Append(field).Append(" IS NOT NULL AND ");

		if (renderedValues.Count > 1)
			_ = _builder.Append('(');

		for (var i = 0; i < renderedValues.Count; i++)
		{
			if (i > 0)
				_ = _builder.Append(" OR ");

			AppendMatch(field, renderedValues[i]);
		}

		if (renderedValues.Count > 1)
			_ = _builder.Append(')');

		_ = _builder.Append(')');
	}

	private static bool IsNullGuard(Expression expression, ParameterExpression element, ExpressionType comparison) =>
		expression is BinaryExpression binary
		&& binary.NodeType == comparison
		&& ((binary.Left == element && IsNullConstant(binary.Right)) || (binary.Right == element && IsNullConstant(binary.Left)));

	private static Expression StripQuotes(Expression expression)
	{
		var current = expression;

		while (current is UnaryExpression { NodeType: ExpressionType.Quote } quote)
			current = quote.Operand;

		return current;
	}

	private static bool TryGetTextPredicateKind(string methodName, out ElementPredicateKind kind)
	{
		switch (methodName)
		{
			case "StartsWith":
				kind = ElementPredicateKind.StartsWith;
				return true;

			case "EndsWith":
				kind = ElementPredicateKind.EndsWith;
				return true;

			case "Contains":
				kind = ElementPredicateKind.Contains;
				return true;

			default:
				kind = default;
				return false;
		}
	}

	/// <summary>
	/// Whether enumerating the collection and comparing its values with ES|QL's equality
	/// answers Contains the way the collection does. Only a collection of a known kind
	/// is taken to: arrays, lists and the ReadOnlyCollection AsReadOnly returns, the LINQ
	/// operators, the immutable and concurrent lists of the base library, and the types
	/// the compiler generates for an iterator method or a collection expression, which
	/// all compare with default equality. A set of any kind carries its own comparer, a
	/// dictionary and its keys likewise, and a collection type of the caller's own may
	/// answer Contains in any way at all: those are refused rather than answered with a
	/// comparison they might not make. A set built with the default comparer is refused
	/// all the same, since telling it apart would take reflection the trimmer cannot follow.
	/// </summary>
	private static bool UsesDefaultEquality(IEnumerable collection)
	{
		var type = collection.GetType();

		// the LINQ operators are the non-public iterator types of System.Linq, in the
		// framework's own assembly; a public type there, such as Lookup, answers Contains
		// its own way
		if (type.IsArray || (type.Namespace == "System.Linq" && !type.IsPublic && type.Assembly == typeof(Enumerable).Assembly))
			return true;

		// an iterator method or a collection expression typed as an interface: the compiler's
		// types only enumerate the values, or hand Contains to an array or a List
		if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
			return true;

		if (!type.IsGenericType)
			return false;

		var definition = type.GetGenericTypeDefinition();

		return definition == typeof(ArraySegment<>)
			|| definition == typeof(ConcurrentBag<>)
			|| definition == typeof(ConcurrentQueue<>)
			|| definition == typeof(ConcurrentStack<>)
			|| definition.FullName is "System.Collections.Immutable.ImmutableArray`1"
				or "System.Collections.Immutable.ImmutableList`1"
			|| definition == typeof(LinkedList<>)
			|| definition == typeof(List<>)
			|| definition == typeof(Queue<>)
			|| definition == typeof(ReadOnlyCollection<>)
			|| definition == typeof(Stack<>);
	}
}
