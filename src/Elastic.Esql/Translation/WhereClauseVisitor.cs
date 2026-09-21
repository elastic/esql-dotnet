// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.Formatting;
using Elastic.Esql.Functions;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ predicate expressions to ES|QL WHERE conditions.
/// </summary>
internal sealed class WhereClauseVisitor(EsqlTranslationContext context) : ExpressionVisitor
{
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));
	private readonly StringBuilder _builder = new();
	private MemberInfo? _comparisonPropertyContext;

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
		// string.CompareOrdinal(a, b) > 0 is the way to order strings in LINQ: rewrite it
		// into a direct comparison, which ES|QL supports natively on keyword fields.
		if (TryVisitStringComparison(node))
			return node;

		if (TryVisitRootNullGuard(node))
			return node;

		if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
		{
			var nullOp = node.NodeType == ExpressionType.Equal ? "IS NULL" : "IS NOT NULL";

			if (IsNullConstant(node.Right))
			{
				_ = Visit(node.Left);
				_ = _builder.Append(' ').Append(nullOp);
				return node;
			}

			if (IsNullConstant(node.Left))
			{
				_ = Visit(node.Right);
				_ = _builder.Append(' ').Append(nullOp);
				return node;
			}
		}

		// Only add parentheses for logical operators (AND/OR) to ensure proper grouping
		var isLogicalOperator = node.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse;

		if (isLogicalOperator)
			_ = _builder.Append('(');

		var enumComparison = TryGetEnumComparison(node);
		if (enumComparison.HasValue && !IsSpecialEnumAccess(enumComparison.Value.MemberSide.Member))
		{
			var propertyMember = enumComparison.Value.MemberSide.Member;
			_ = Visit(enumComparison.Value.MemberSide);
			var op = GetOperator(node.NodeType);
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
			_comparisonPropertyContext = ExtractEntityPropertyMember(node);
			_ = Visit(node.Left);
			var op = GetOperator(node.NodeType);
			_ = _builder.Append(' ').Append(op).Append(' ');
			_ = Visit(node.Right);
			_comparisonPropertyContext = null;
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

		return TryExtract(node.Left) ?? TryExtract(node.Right);

		static MemberInfo? TryExtract(Expression expr)
		{
			var unwrapped = expr.UnwrapConvertExpressions();
			if (unwrapped is MemberExpression member && ExpressionTranslationHelpers.IsRootedInParameter(member))
				return member.Member;

			return null;
		}
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
		// Check if this is accessing a captured variable (closure)
		if (node.Expression is ConstantExpression constantExpression)
		{
			var value = GetMemberValue(node, constantExpression.Value);
			_ = _builder.Append(_context.GetValueOrParameterName(node.Member.Name, value, _comparisonPropertyContext));
			_comparisonPropertyContext = null;
			return node;
		}

		// Check if this is a nested member access on a captured variable
		if (node.Expression is MemberExpression innerMember &&
			innerMember.Expression is ConstantExpression innerConstant)
		{
			var innerValue = GetMemberValue(innerMember, innerConstant.Value);
			var value = GetMemberValue(node, innerValue);
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

	/// <summary>
	/// The column a field expression names, with the compiler's transparent-identifier
	/// prefixes stripped the way ordinary field predicates do.
	/// </summary>
	private string ResolveMultiValueField(Expression expression) =>
		expression is MemberExpression member
			? ResolveFieldPath(member)
			: expression.ResolveFieldName(_context.Metadata);

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
		var translated = EsqlFunctionTranslator.TryTranslateMethodCall(methodCall, TranslateDateTimeExpression);
		return translated ?? throw new NotSupportedException($"EsqlFunction {methodName} is not supported in DateTime context.");
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
			return VisitEsqlFunction(node);

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
		var saved = _builder.ToString();
		_ = _builder.Clear();
		_ = Visit(expression);
		var result = _builder.ToString();
		_ = _builder.Clear().Append(saved);
		return result;
	}

	/// <summary>
	/// Whether the StringComparison argument asks for the ordering ES|QL performs.
	/// Only <see cref="StringComparison.Ordinal"/> does: keyword ordering is
	/// case-sensitive, so OrdinalIgnoreCase would order "a" and "B" the other way.
	/// </summary>
	private static bool IsOrdinalComparison(Expression expression)
	{
		try
		{
			return GetConstantValue(expression) is StringComparison.Ordinal;
		}
		catch (NotSupportedException)
		{
			return false;
		}
	}

	/// <summary>
	/// "p != null" on the lambda parameter itself: the document is never null, and
	/// there is no field to put in front of IS NOT NULL, so the guard is a constant.
	/// </summary>
	private bool TryVisitRootNullGuard(BinaryExpression node)
	{
		if (node.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
			return false;

		var parameter = node.Left is ParameterExpression left && ResolvesToNullConstant(node.Right) ? left
			: node.Right is ParameterExpression right && ResolvesToNullConstant(node.Left) ? right
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

		_ = _builder.Append(node.NodeType == ExpressionType.Equal ? "FALSE" : "TRUE");
		return true;
	}

	/// <summary>Whether the parameter stands for the document row rather than a projected value.</summary>
	private bool IsDocumentParameter(ParameterExpression parameter) =>
		!parameter.Type.IsValueType
		&& parameter.Type != typeof(string)
		&& (_context.ElementType is null || parameter.Type == _context.ElementType)
		// Matching the element type is not enough: a recursive type projects to itself,
		// as in ".Select(n => n.Child)" over "Node.Child : Node?", and the projected
		// value may well be null. Keep and Drop narrow the columns but leave the row,
		// so the question is whether a Select has run, not which commands were emitted.
		&& !_context.HasProjected;

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
	/// translation is exact. Only that case is translated; a value outside it, or two
	/// fields compared with each other, is refused rather than ordered wrongly.
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

		if (call is null || zero is not ConstantExpression { Value: 0 })
			return false;

		if (call.Method.DeclaringType != typeof(string)
			|| call.Method.Name is not ("CompareTo" or "Compare" or "CompareOrdinal"))
			return false;

		// From here the shape is the supported one, so anything refused is refused with
		// its own reason rather than the generic "only when compared to zero" message.
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

		var first = call.Arguments[0];
		var second = call.Arguments[1];

		if (parameters.Length == 3 && !IsOrdinalComparison(call.Arguments[2]))
		{
			throw new NotSupportedException(
				$"String method {call.Method.Name} is only supported with StringComparison.Ordinal: "
				+ "keyword values are ordered by their UTF-8 bytes and comparison is "
				+ "case-sensitive, so any other comparison mode asks for an ordering "
				+ "Elasticsearch does not apply.");
		}

		// .NET orders a non-null string above null, which a plain ES|QL comparison
		// against null does not reproduce; there is a field to test for null instead
		if (ResolvesToNullConstant(first) || ResolvesToNullConstant(second))
		{
			throw new NotSupportedException(
				$"String method {call.Method.Name} against null is not supported: compare the "
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
			throw new NotSupportedException(
				$"String method {call.Method.Name} between two fields is not supported: without a "
				+ "value to look at, the translation cannot tell whether the UTF-16 ordering of .NET "
				+ "and the UTF-8 ordering of Elasticsearch agree on the comparison.");
		}

		if (text.Any(character => char.IsSurrogate(character) || character >= '\uE000'))
		{
			throw new NotSupportedException(
				$"String method {call.Method.Name} against a value holding a character at or above "
				+ "U+E000, or outside the Basic Multilingual Plane, is not supported: on such a value "
				+ "the UTF-16 ordering of .NET and the UTF-8 ordering of Elasticsearch can disagree, "
				+ "so the comparison is left untranslated rather than answered with the wrong order.");
		}

		var op = node.NodeType switch
		{
			ExpressionType.GreaterThan => flipped ? "<" : ">",
			ExpressionType.GreaterThanOrEqual => flipped ? "<=" : ">=",
			ExpressionType.LessThan => flipped ? ">" : "<",
			_ => flipped ? ">=" : "<="
		};

		// .NET orders null before every string. A comparison against a missing field is
		// null in ES|QL and drops the row, so a field that can be missing has its side of
		// the ordering spelled out.
		// one side is a value by now, so at most one side is a field that can be missing.
		// Its ordering can be spelled out for the field itself, not for an expression of
		// it, whose value for a missing field is not the field's null.
		var firstMayBeMissing = AsNullableField(first);
		var secondMayBeMissing = AsNullableField(second);

		if ((firstMayBeMissing is null && ReadsANullableField(first)) || (secondMayBeMissing is null && ReadsANullableField(second)))
		{
			throw new NotSupportedException(
				$"String method {call.Method.Name} over an expression of a field that can be missing is "
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

		_ = Visit(first);
		_ = _builder.Append(' ').Append(op).Append(' ');
		_ = Visit(second);

		if (guardedField is not null)
			_ = _builder.Append(')');

		return true;
	}

	private string? AsNullableField(Expression expression) => expression switch
	{
		MemberExpression member when IsNullableFieldMember(member) => ResolveFieldPath(member),
		// a multi-field of a nullable member is missing when the member is: the guard
		// goes on the path as emitted, member and multi-field name together
		MethodCallExpression { Method.Name: "MultiField", Arguments: [MemberExpression member, ConstantExpression { Value: string }] } call
			when call.Method.DeclaringType == typeof(GeneralPurposeExtensions) && IsNullableFieldMember(member)
			=> call.ResolveFieldName(_context.Metadata),
		_ => null
	};

	private static bool IsNullableFieldMember(MemberExpression member) =>
		ExpressionTranslationHelpers.IsRootedInParameter(member) && IsDeclaredNullable(member.Member);

	/// <summary>Whether any member read anywhere in the expression is a field that can be missing.</summary>
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
			if (IsNullableFieldMember(node))
				Found = true;

			return base.VisitMember(node);
		}
	}

	/// <summary>
	/// Whether the member is declared as a nullable reference. Only then is the guard
	/// worth writing: on a non-nullable member the comparison already reads the way the
	/// source does.
	/// <para>
	/// The compiler records nullability as a <c>NullableAttribute</c> on the member, or
	/// omits it and records a <c>NullableContextAttribute</c> on the declaring type when
	/// every member shares the same annotation. Both are read from the attribute data
	/// rather than by instantiating the attribute, which keeps the check out of the
	/// trimmer's way.
	/// </para>
	/// </summary>
	/// <summary>
	/// Whether a constructor parameter is declared as a nullable reference, read the same
	/// way: its own attribute, then the context of the constructor and its declaring types.
	/// </summary>
	internal static bool IsDeclaredNullable(ParameterInfo parameter)
	{
		if (parameter.ParameterType.IsValueType)
			return false;

		var own = NullableFlag(parameter.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableAttribute");

		if (own is not null)
			return own == 2;

		var context = NullableFlag(parameter.Member.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableContextAttribute");

		if (context is not null)
			return context == 2;

		for (var declaring = parameter.Member.DeclaringType; declaring is not null; declaring = declaring.DeclaringType)
		{
			context = NullableFlag(declaring.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableContextAttribute");

			if (context is not null)
				return context == 2;
		}

		return false;
	}

	internal static bool IsDeclaredNullable(MemberInfo member)
	{
		var type = member switch
		{
			PropertyInfo property => property.PropertyType,
			FieldInfo field => field.FieldType,
			_ => null
		};

		if (type is null || type.IsValueType)
			return false;

		var own = NullableFlag(member.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableAttribute");

		if (own is not null)
			return own == 2;

		for (var declaring = member.DeclaringType; declaring is not null; declaring = declaring.DeclaringType)
		{
			var context = NullableFlag(declaring.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableContextAttribute");

			if (context is not null)
				return context == 2;
		}

		return false;
	}

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

	private Expression VisitStringMethod(MethodCallExpression node)
	{
		var methodName = node.Method.Name;

		switch (methodName)
		{
			case "Contains":
				// string.Contains("x") → LIKE "*x*"
				_ = Visit(node.Object);
				_ = _builder.Append(" LIKE ");
				var containsValue = GetConstantValue(node.Arguments[0]);
				_ = _builder.Append(RenderPattern("*" + EscapeLikeMetacharacters(containsValue?.ToString() ?? "") + "*"));
				break;

			case "StartsWith":
				// string.StartsWith("x") → LIKE "x*"
				_ = Visit(node.Object);
				_ = _builder.Append(" LIKE ");
				var startsValue = GetConstantValue(node.Arguments[0]);
				_ = _builder.Append(RenderPattern(EscapeLikeMetacharacters(startsValue?.ToString() ?? "") + "*"));
				break;

			case "EndsWith":
				// string.EndsWith("x") → LIKE "*x"
				_ = Visit(node.Object);
				_ = _builder.Append(" LIKE ");
				var endsValue = GetConstantValue(node.Arguments[0]);
				_ = _builder.Append(RenderPattern("*" + EscapeLikeMetacharacters(endsValue?.ToString() ?? "")));
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
				_ = Visit(node.Object);
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

	/// <summary>A predicate over one value of a multi-value field, e.g. "x == 42".</summary>
	/// <param name="Kind">The comparison the predicate makes.</param>
	/// <param name="Values">The values it compares against, one for a comparison, any number for In.</param>
	/// <param name="Negated">Whether the predicate was written under a NOT, which moves between Any and All.</param>
	/// <param name="Names">
	/// The captured variable each value came from, where it came from one, so the
	/// value can be emitted as a query parameter rather than inlined.
	/// </param>
	private readonly record struct ElementPredicate(
		ElementPredicateKind Kind,
		IReadOnlyList<object?> Values,
		bool Negated,
		IReadOnlyList<string?>? Names = null);

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
		var methodName = node.Method.Name;

		if (methodName is not ("Any" or "All" or "Contains"))
			return false;

		// the source must be a document field, not a constant collection
		var source = node.Method.IsStatic
			? node.Arguments.Count > 0 ? node.Arguments[0] : null
			: node.Object;

		// arrays reach us through MemoryExtensions.Contains(ReadOnlySpan<T>, T)
		if (source is not null && node.Method.DeclaringType == typeof(MemoryExtensions))
			source = TryUnwrapMemoryExtensionsSource(source);

		if (source is null || !IsMultiValueField(source))
			return false;

		// only the framework's own Any, All and Contains: a method of that name defined
		// elsewhere may mean anything, and is left to fail soft as before
		if (!IsFrameworkMethod(node.Method))
			return false;

		// only a field declared as a list or an array of a known kind: a set answers
		// Contains by the comparer it was built with, which the declared type does not
		// show, and a collection type of the document's own may answer it any way at all
		if (!IsListType(source.Type))
		{
			throw new NotSupportedException(
				$"A predicate over a field of type {TypeName(source.Type)} is not supported: only a field "
				+ "declared as an array or a list of the base library is taken to compare with default "
				+ "equality. A set answers Contains by the comparer it was built with, and a collection "
				+ "type of your own may answer it in any way, neither of which the translation can see.");
		}

		// field.Any() with no predicate: the field simply has to hold a value
		if (methodName == "Any" && node.Arguments.Count == (node.Method.IsStatic ? 1 : 0))
		{
			// MV_COUNT is null over a missing field, and so would be the negation; LINQ
			// reads a missing field as an empty sequence, where Any() is simply false
			_ = _builder.Append("COALESCE(MV_COUNT(").Append(ResolveMultiValueField(source)).Append("), 0) > 0");
			return true;
		}

		// field.Contains(value), and only that overload: one taking a comparer asks
		// for a comparison the translation cannot honour
		if (methodName == "Contains")
		{
			// field.Contains(x) is answered by the collection behind the field, and an
			// interface says nothing about which; Any(t => t == x) compares the elements
			// themselves, whatever holds them, so it is the shape to use there
			if (source.Type.IsInterface)
			{
				throw new NotSupportedException(
					$"Contains over a field of type {TypeName(source.Type)} is not supported: an interface "
					+ "does not say how the collection behind it answers Contains. Use "
					+ "Any(t => t == value), which compares the elements themselves.");
			}

			var expectedArguments = node.Method.IsStatic ? 2 : 1;

			return node.Arguments.Count == expectedArguments
				&& TryAppendMatch(source, node.Arguments[^1]);
		}

		var argument = node.Arguments[^1];

		if (StripQuotes(argument) is not LambdaExpression { Parameters.Count: 1 } lambda)
			return false;

		var predicate = TryParseElementPredicate(lambda.Body, lambda.Parameters[0]);
		if (predicate is null)
			return false;

		return TryAppendQuantified(source, lambda.Parameters[0].Type, methodName == "All", predicate.Value);
	}

	/// <summary>
	/// Reads the body of the lambda passed to Any/All as one predicate on the element.
	/// Null guards on the element are dropped, since a stored value is never null.
	/// </summary>
	private static ElementPredicate? TryParseElementPredicate(Expression body, ParameterExpression element, bool negated = false)
	{
		switch (body)
		{
			case UnaryExpression { NodeType: ExpressionType.Not } negation:
				return TryParseElementPredicate(negation.Operand, element, !negated);

			// "x != null && P(x)" is P(x)
			case BinaryExpression { NodeType: ExpressionType.AndAlso } conjunction when IsNullGuard(conjunction.Left, element, ExpressionType.NotEqual):
				return TryParseElementPredicate(conjunction.Right, element, negated);

			case BinaryExpression { NodeType: ExpressionType.AndAlso } conjunction when IsNullGuard(conjunction.Right, element, ExpressionType.NotEqual):
				return TryParseElementPredicate(conjunction.Left, element, negated);

			// "x == null || P(x)" is P(x)
			case BinaryExpression { NodeType: ExpressionType.OrElse } disjunction when IsNullGuard(disjunction.Left, element, ExpressionType.Equal):
				return TryParseElementPredicate(disjunction.Right, element, negated);

			case BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual } comparison:
				{
					var value = comparison.Left == element ? comparison.Right
						: comparison.Right == element ? comparison.Left
						: null;

					if (value is null || !TryGetConstant(value, out var constant))
						return null;

					var isEqual = comparison.NodeType == ExpressionType.Equal;
					return new ElementPredicate(ElementPredicateKind.Equal, [constant], isEqual ? negated : !negated, [CapturedName(value)]);
				}

			case BinaryExpression
			{
				NodeType: ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
					or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
			} ordering:
				{
					// "10 < x" is "x > 10": keep the element on the left
					var elementOnLeft = ordering.Left == element;
					var value = elementOnLeft ? ordering.Right : ordering.Right == element ? ordering.Left : null;

					if (value is null || !TryGetConstant(value, out var constant))
						return null;

					var kind = (ordering.NodeType, elementOnLeft) switch
					{
						(ExpressionType.GreaterThan, true) or (ExpressionType.LessThan, false) => ElementPredicateKind.GreaterThan,
						(ExpressionType.GreaterThanOrEqual, true) or (ExpressionType.LessThanOrEqual, false) => ElementPredicateKind.GreaterThanOrEqual,
						(ExpressionType.LessThan, true) or (ExpressionType.GreaterThan, false) => ElementPredicateKind.LessThan,
						_ => ElementPredicateKind.LessThanOrEqual
					};

					return new ElementPredicate(kind, [constant], negated, [CapturedName(value)]);
				}

			case MethodCallExpression call:
				{
					// x.StartsWith("a"), x.EndsWith("a"), x.Contains("a")
					if (call.Object == element && call.Method.DeclaringType == typeof(string) && call.Arguments.Count == 1)
					{
						if (!TryGetTextPredicateKind(call.Method.Name, out var kind)
							|| !TryGetConstant(call.Arguments[0], out var constant))
							return null;

						return new ElementPredicate(kind, [constant], negated, [CapturedName(call.Arguments[0])]);
					}

					// values.Contains(x), over a constant collection
					if (TryGetContainsArguments(call, out var valueExpression, out var collection)
						&& valueExpression == element
						&& collection is not null)
					{
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
							return null;

						return new ElementPredicate(ElementPredicateKind.In, candidates, negated);
					}

					return null;
				}

			default:
				return null;
		}
	}

	/// <summary>The kind of the string predicates MATCH cannot answer on its own.</summary>
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

	private static bool IsNullGuard(Expression expression, ParameterExpression element, ExpressionType comparison) =>
		expression is BinaryExpression binary
		&& binary.NodeType == comparison
		&& ((binary.Left == element && IsNullConstant(binary.Right)) || (binary.Right == element && IsNullConstant(binary.Left)));

	/// <summary>
	/// Whether enumerating the collection and comparing its values with ES|QL's equality
	/// answers Contains the way the collection does. Only a collection of a known kind
	/// is taken to: arrays, lists, the LINQ operators, and the immutable and concurrent
	/// lists of the base library, which all compare with default equality. A set of any
	/// kind carries its own comparer, a dictionary and its keys likewise, a collection
	/// type of the caller's own may answer Contains in any way at all, and so may a
	/// wrapper such as ReadOnlyCollection, which hands Contains to the list it wraps:
	/// those are refused rather than answered with a comparison they might not make. A
	/// set built with the default comparer is refused all the same, since telling it
	/// apart would take reflection the trimmer cannot follow.
	/// </summary>
	private static bool UsesDefaultEquality(IEnumerable collection)
	{
		var type = collection.GetType();

		// the LINQ operators are the non-public iterator types of System.Linq, in the
		// framework's own assembly; a public type there, such as Lookup, answers Contains
		// its own way
		if (type.IsArray || (type.Namespace == "System.Linq" && !type.IsPublic && type.Assembly == typeof(Enumerable).Assembly))
			return true;

		if (!type.IsGenericType)
			return false;

		var definition = type.GetGenericTypeDefinition();

		return definition == typeof(List<>)
			|| definition == typeof(Queue<>)
			|| definition == typeof(Stack<>)
			|| definition == typeof(LinkedList<>)
			|| definition == typeof(ArraySegment<>)
			|| definition == typeof(ConcurrentBag<>)
			|| definition == typeof(ConcurrentQueue<>)
			|| definition == typeof(ConcurrentStack<>)
			|| definition.FullName is "System.Collections.Immutable.ImmutableArray`1"
				or "System.Collections.Immutable.ImmutableList`1";
	}

	/// <summary>The name of a type without the arity a generic one carries, for a message.</summary>
	private static string TypeName(Type type) =>
		type.Name.IndexOf('`') is var arity and >= 0 ? type.Name.Substring(0, arity) : type.Name;

	/// <summary>The captured variable an expression reads, when it reads one.</summary>
	private static string? CapturedName(Expression expression) =>
		expression is MemberExpression { Expression: ConstantExpression or MemberExpression } member
			? member.Member.Name
			: null;

	/// <summary>
	/// The constant a predicate compares a field value against. Null is refused: a
	/// multi-value field stores no null element, so there is nothing to match, and
	/// MATCH(field, null) is not valid ES|QL.
	/// </summary>
	private static bool TryGetConstant(Expression expression, out object? value)
	{
		try
		{
			value = GetConstantValue(expression);
			return value is not null;
		}
		catch (NotSupportedException)
		{
			value = null;
			return false;
		}
	}

	/// <summary>
	/// Any(P) and All(P) over the values of a field. A negated predicate is pushed into
	/// the quantifier, since Any(not P) is "not All(P)" and All(not P) is "not Any(P)".
	/// </summary>
	private bool TryAppendQuantified(Expression field, Type elementType, bool all, ElementPredicate predicate)
	{
		var name = ResolveMultiValueField(field);

		// "Any(not P)" is "not All(P)" and "All(not P)" is "not Any(P)": the negation
		// moves onto the quantifier, which flips
		if (predicate.Negated)
		{
			_ = _builder.Append("NOT ");
			all = !all;
			predicate = predicate with { Negated = false };
		}

		switch (predicate.Kind)
		{
			// a document matches MATCH when any of the field's values does
			case ElementPredicateKind.Equal when !all:
				AppendMatch(name, RenderValue(predicate, 0));
				return true;

			case ElementPredicateKind.In when !all:
				if (predicate.Values.Count == 0)
				{
					_ = _builder.Append("FALSE");
					return true;
				}

				// each value adds one level to the expression, as each position does
				if (predicate.Values.Count > EsqlQueryableExtensions.MaxMultiValueLimit)
				{
					throw new NotSupportedException(
						$"A collection of {predicate.Values.Count} values is not supported here: each value adds "
						+ $"a level to the expression Elasticsearch parses, and at most {EsqlQueryableExtensions.MaxMultiValueLimit} fit.");
				}

				if (predicate.Values.Count > 1)
					_ = _builder.Append('(');

				for (var i = 0; i < predicate.Values.Count; i++)
				{
					if (i > 0)
						_ = _builder.Append(" OR ");

					AppendMatch(name, RenderValue(predicate, i));
				}

				if (predicate.Values.Count > 1)
					_ = _builder.Append(')');

				return true;

			// every value equals v: the field holds one distinct value, and it matches.
			// A missing field has no value that differs, as All() over an empty sequence is true.
			case ElementPredicateKind.Equal:
				_ = _builder.Append('(').Append(name).Append(" IS NULL OR (MV_COUNT(MV_DEDUPE(").Append(name).Append(")) == 1 AND ");
				AppendMatch(name, RenderValue(predicate, 0));
				_ = _builder.Append("))");
				return true;

			// some value is above v when the largest is; every value is when the smallest is
			case ElementPredicateKind.GreaterThan or ElementPredicateKind.GreaterThanOrEqual
				or ElementPredicateKind.LessThan or ElementPredicateKind.LessThanOrEqual:
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
						.Append(RenderValue(predicate, 0)).Append(')');

					return true;
				}

			default:
				return TryAppendValuePattern(name, elementType, all, predicate);
		}
	}

	/// <summary>
	/// A LIKE pattern, always as a literal: ES|QL takes a literal after LIKE and rejects
	/// a parameter there, where a function argument such as the prefix of STARTS_WITH
	/// may be one. The pattern's own escapes go in first, the literal's on top of them.
	/// </summary>
	private static string RenderPattern(string pattern) => EsqlFormatting.FormatString(pattern);

	/// <summary>
	/// A value of an element predicate, as a query parameter when it came from a
	/// captured variable and <c>InlineParameters</c> is off, and as a literal otherwise.
	/// </summary>
	private string RenderValue(ElementPredicate predicate, int index)
	{
		var name = predicate.Names is { } names && index < names.Count ? names[index] : null;

		return name is null
			? _context.FormatValue(predicate.Values[index], null)
			: _context.GetValueOrParameterName(name, predicate.Values[index]);
	}

	private void AppendMatch(string field, string renderedValue) =>
		_ = _builder.Append("MATCH(").Append(field).Append(", ").Append(renderedValue).Append(')');

	/// <summary>
	/// Predicates MATCH cannot answer, such as "starts with". ES|QL applies a scalar
	/// function to a single value, not to every value of a field at once, but MV_SLICE
	/// reads a value by position, so the test is written out once per position and
	/// combined: any of them for Any, all of them for All.
	/// <para>
	/// Only as many positions as the caller stated with <c>MultiValueLimit</c> are read,
	/// so a field holding more values than that cannot be decided from them. The predicate
	/// is null for such a field rather than false, since false would let an enclosing NOT
	/// turn it into a match, and WHERE drops a null row either way.
	/// </para>
	/// </summary>
	private bool TryAppendValuePattern(string field, Type elementType, bool all, ElementPredicate predicate)
	{
		var isString = elementType == typeof(string);
		var isIntegral = elementType == typeof(int) || elementType == typeof(long)
			|| elementType == typeof(short) || elementType == typeof(byte);

		// text predicates need text; equality needs a value whose text form is
		// unambiguous, which rules out floating-point fields
		if (!isString && (!isIntegral || predicate.Kind is not (ElementPredicateKind.Equal or ElementPredicateKind.In)))
			return false;

		// numbers are rendered the way Elasticsearch renders them, which is invariant;
		// the current culture could otherwise introduce separators of its own
		var values = predicate.Values
			.Select(value => value is IFormattable formattable
				? formattable.ToString(null, CultureInfo.InvariantCulture)
				: value?.ToString() ?? "")
			.ToList();

		// All over an empty list holds only for the empty field; Any never does
		if (predicate.Kind == ElementPredicateKind.In && values.Count == 0)
		{
			_ = _builder.Append(all ? field + " IS NULL" : "FALSE");
			return true;
		}

		// The number of positions read has to be fixed when the query is written, and a
		// document holding more values is left out of the result: that is a contract the
		// caller states with MultiValueLimit, not one the translation may assume.
		var positions = _context.MultiValueLimit
			?? throw new NotSupportedException(
				"A predicate over the individual values of a multi-value field, such as "
				+ "StartsWith or All over a list, reads the field one position at a time and "
				+ "needs to know how many to read: state it with MultiValueLimit(n). A document "
				+ "holding more values than that is then left out of the result.");

		// under In the values are written out inside every position as an OR chain, and
		// a chain of n values adds n - 1 levels to the positions' own; a text predicate
		// tests one value per position and adds none
		if (predicate.Kind == ElementPredicateKind.In && positions + values.Count - 1 > EsqlQueryableExtensions.MaxMultiValueLimit)
		{
			throw new NotSupportedException(
				$"{positions} positions and {values.Count} values together are more than the expression "
				+ $"Elasticsearch parses allows: at most {EsqlQueryableExtensions.MaxMultiValueLimit} of both.");
		}

		// Rendered once, before the positions, so a captured value becomes one parameter
		// rather than one per position. Anything but a string field is compared through
		// TO_STRING, so its values are rendered as text rather than in their own type.
		var rendered = values
			.Select((value, index) => predicate.Kind == ElementPredicateKind.Contains
				? RenderPattern("*" + EscapeLikeMetacharacters(value) + "*")
				: isString
					? RenderValue(predicate, index)
					: EsqlFormatting.FormatString(value))
			.ToList();

		// A field holding more values than the positions read cannot be answered from
		// those positions alone, and "false" would let an enclosing NOT turn it into a
		// match. The predicate is null there instead, which WHERE drops either way, so
		// such a document is left out of the result rather than answered wrongly.
		_ = _builder.Append("CASE(MV_COUNT(").Append(field).Append(") > ").Append(positions).Append(", NULL, (");

		for (var position = 0; position < positions; position++)
		{
			if (position > 0)
				_ = _builder.Append(all ? " AND " : " OR ");

			var value = $"MV_SLICE({field}, {position}, {position})";

			// Past the last value MV_SLICE is null, and so would be the test, leaving
			// the whole predicate undefined instead of answering either way. An absent
			// value satisfies All and does not satisfy Any, so it is spelled out: the
			// result then stays definite under an enclosing NOT.
			_ = _builder.Append("COALESCE(");

			if (all)
				_ = _builder.Append(value).Append(" IS NULL OR ");

			AppendValuePredicate(value, isString, predicate.Kind, rendered);

			_ = _builder.Append(", ").Append(all ? "true" : "false").Append(')');
		}

		// closes the positions, then the CASE
		_ = _builder.Append("))");
		return true;
	}

	/// <summary>
	/// Escapes what a LIKE pattern reads as a wildcard, leaving the string literal's own
	/// escaping to <see cref="EsqlFormatting.FormatString"/>.
	/// </summary>
	private static string EscapeLikeMetacharacters(string value) =>
		value.Replace("\\", "\\\\").Replace("*", "\\*").Replace("?", "\\?");

	/// <summary>One value of a multi-value field, tested against the rendered values.</summary>
	private void AppendValuePredicate(string value, bool isString, ElementPredicateKind kind, IReadOnlyList<string> rendered)
	{
		var text = isString ? value : $"TO_STRING({value})";

		switch (kind)
		{
			case ElementPredicateKind.StartsWith:
				_ = _builder.Append("STARTS_WITH(").Append(text).Append(", ").Append(rendered[0]).Append(')');
				break;

			case ElementPredicateKind.EndsWith:
				_ = _builder.Append("ENDS_WITH(").Append(text).Append(", ").Append(rendered[0]).Append(')');
				break;

			case ElementPredicateKind.Contains:
				_ = _builder.Append(text).Append(" LIKE ").Append(rendered[0]);
				break;

			case ElementPredicateKind.In:
				_ = _builder.Append('(');

				for (var i = 0; i < rendered.Count; i++)
				{
					if (i > 0)
						_ = _builder.Append(" OR ");

					_ = _builder.Append(text).Append(" == ").Append(rendered[i]);
				}

				_ = _builder.Append(')');
				break;

			default:
				_ = _builder.Append(text).Append(" == ").Append(rendered[0]);
				break;
		}
	}

	private bool TryAppendMatch(Expression field, Expression value)
	{
		if (!TryGetConstant(value, out var constant))
			return false;

		var name = CapturedName(value);

		AppendMatch(
			ResolveMultiValueField(field),
			name is null
				? _context.FormatValue(constant, null)
				: _context.GetValueOrParameterName(name, constant));
		return true;
	}

	/// <summary>A collection-typed member of the document, as opposed to a captured constant.</summary>
	private static bool IsMultiValueField(Expression expression) =>
		IsEnumerableType(expression.Type) && ContainsParameter(expression);

	/// <summary>
	/// Whether the method is the framework's own: Enumerable and MemoryExtensions for the
	/// static forms, the collections of the base library for the instance ones.
	/// </summary>
	private static bool IsFrameworkMethod(MethodInfo method)
	{
		var declaring = method.DeclaringType;

		if (declaring is null)
			return false;

		if (method.IsStatic)
			return declaring == typeof(Enumerable) || declaring == typeof(MemoryExtensions);

		return declaring.Assembly == typeof(object).Assembly
			|| declaring.Assembly.GetName().Name is "System.Collections" or "System.Collections.Immutable";
	}

	/// <summary>
	/// A field declared as an array, or as a list of the base library, by its declared
	/// type: the kinds known to compare with default equality, as the captured
	/// collections are held to the kinds known to.
	/// </summary>
	private static bool IsListType(Type type)
	{
		if (type.IsArray)
			return true;

		if (!type.IsGenericType)
			return false;

		var definition = type.GetGenericTypeDefinition();

		return definition == typeof(List<>)
			|| definition == typeof(IList<>)
			|| definition == typeof(IReadOnlyList<>)
			|| definition == typeof(ICollection<>)
			|| definition == typeof(IReadOnlyCollection<>)
			|| definition == typeof(IEnumerable<>)
			|| definition.FullName is "System.Collections.Immutable.ImmutableArray`1"
				or "System.Collections.Immutable.ImmutableList`1";
	}

	private static bool ContainsParameter(Expression expression) => expression switch
	{
		ParameterExpression => true,
		MemberExpression member => member.Expression is not null && ContainsParameter(member.Expression),
		UnaryExpression unary => ContainsParameter(unary.Operand),
		MethodCallExpression call => call.Object is not null && ContainsParameter(call.Object),
		_ => false
	};

	private static Expression StripQuotes(Expression expression)
	{
		var current = expression;

		while (current is UnaryExpression { NodeType: ExpressionType.Quote } quote)
			current = quote.Operand;

		return current;
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

	private static bool TryGetContainsArguments(MethodCallExpression node, out Expression valueExpression, out IEnumerable? collection)
	{
		valueExpression = null!;
		collection = null;

		if (node.Method.IsStatic)
		{
			if (node.Method.DeclaringType == typeof(Enumerable) && node.Arguments.Count == 2)
			{
				valueExpression = node.Arguments[1];
				return TryGetCollectionValue(node.Arguments[0], out collection);
			}

			if (node.Method.DeclaringType == typeof(MemoryExtensions) && node.Arguments.Count == 2)
			{
				var source = TryUnwrapMemoryExtensionsSource(node.Arguments[0]);
				if (source is null)
					return false;

				valueExpression = node.Arguments[1];
				return TryGetCollectionValue(source, out collection);
			}

			return false;
		}

		if (node.Object is null || node.Arguments.Count != 1 || !IsEnumerableType(node.Object.Type))
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

		return IsEnumerableType(current.Type) ? current : null;
	}

	private static bool IsEnumerableType(Type type) =>
		type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

	private static bool TryGetCollectionValue(Expression expression, out IEnumerable? collection)
	{
		collection = null;

		if (!IsEnumerableType(expression.Type))
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

	/// <summary>A null literal, or a captured variable that holds null.</summary>
	private static bool ResolvesToNullConstant(Expression expression)
	{
		if (IsNullConstant(expression))
			return true;

		if (expression is not (MemberExpression or UnaryExpression { NodeType: ExpressionType.Convert }))
			return false;

		try
		{
			return GetConstantValue(expression) is null;
		}
		catch (NotSupportedException)
		{
			return false;
		}
	}

	private static bool IsNullConstant(Expression expression) =>
		expression is ConstantExpression { Value: null };
}
