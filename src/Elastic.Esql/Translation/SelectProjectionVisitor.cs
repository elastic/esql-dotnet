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

				var declaringType = member.DeclaringType ?? node.Type;
				var resultField = _context.ResolveFieldName(declaringType, member);
				_ = anonymousFieldNames?.Add(resultField);

				ClassifyProjectionMember(resultField, arg, CanHoldNull(member), member.Name);
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

			// the constructor's parameter is the member here: its own nullability says
			// whether the null a dropped guard produces can reach the row
			ClassifyProjectionMember(jsonProp.Name, node.Arguments[i], CanHoldNull(parameters[i]), paramName);
		}

		return node;
	}

	protected override Expression VisitMemberInit(MemberInitExpression node)
	{
		foreach (var binding in node.Bindings)
		{
			if (binding is MemberAssignment assignment)
			{
				var declaringType = assignment.Member.DeclaringType ?? node.Type;
				var resultField = _context.ResolveFieldName(declaringType, assignment.Member);
				ClassifyProjectionMember(resultField, assignment.Expression, CanHoldNull(assignment.Member), assignment.Member.Name);
			}
		}

		return node;
	}

	protected override Expression VisitMember(MemberExpression node)
	{
		// Top-level EsqlMetadata.X access in a projection (rare; usually appears inside a NewExpression)
		if (node.Expression is null && node.Member.DeclaringType == typeof(EsqlMetadata))
		{
			var metaName = _context.ResolveMetadataMemberOrThrow(node.Member.Name);
			_projections.Add(new ProjectionEntry(ProjectionKind.Keep, metaName, metaName, null));
			return node;
		}

		var fieldName = node.ResolveFieldName(_context.Metadata);
		if (ExpressionTranslationHelpers.IsObjectSelectionType(node.Type))
			fieldName = $"{fieldName}.*";

		_projections.Add(new ProjectionEntry(ProjectionKind.Keep, fieldName, fieldName, null));

		return node;
	}

	private void ClassifyProjectionMember(string resultField, Expression sourceExpression, bool targetCanHoldNull = true, string? targetName = null)
	{
		// A null-guarded nested projection, the shape a GraphQL layer emits for
		// "parent { child }": param == null ? null : new Child { Field = param.Child.Field }
		if (sourceExpression is ConditionalExpression guarded
			&& TryUnwrapNullGuard(guarded, out var guardedBranch)
			&& guardedBranch is MemberInitExpression or NewExpression)
		{
			// Dropping the guard leaves no column to say the child is missing: a missing
			// parent comes back as whatever the member holds by default, which is null
			// only for a member declared nullable. Anywhere else the null the guard
			// produces has no way to reach the row, so the shape is refused.
			if (!targetCanHoldNull)
			{
				throw new NotSupportedException(
					$"A null guard around {targetName} cannot be translated: the member is not declared "
					+ "nullable, so the null the guard produces for a missing parent has no way to reach "
					+ "the materialized row. Declare it nullable, without an initializer.");
			}

			if (TryClassifyNestedProjection(resultField, guardedBranch))
				return;
		}

		if (sourceExpression is UnaryExpression { NodeType: ExpressionType.Convert } unary && IsNullableCast(unary))
		{
			ClassifyProjectionMember(resultField, unary.Operand);
			return;
		}

		if (TryClassifyNestedProjection(resultField, sourceExpression))
			return;

		// EsqlMetadata.X used as a projection source -> rename _x AS resultField (or keep when name matches).
		if (sourceExpression is MemberExpression { Expression: null, Member: { } metaMember }
			&& metaMember.DeclaringType == typeof(EsqlMetadata))
		{
			var metaName = _context.ResolveMetadataMemberOrThrow(metaMember.Name);
			_projections.Add(metaName == resultField
				? new ProjectionEntry(ProjectionKind.Keep, metaName, metaName, null)
				: new ProjectionEntry(ProjectionKind.Rename, resultField, metaName, null));
			return;
		}

		// EsqlMetadata.SourceAs<T>() -> KEEP _source (target type is reflected by the destination property).
		if (sourceExpression is MethodCallExpression
			{
				Method: { Name: nameof(EsqlMetadata.SourceAs), DeclaringType: var sourceAsDecl }
			}
			&& sourceAsDecl == typeof(EsqlMetadata))
		{
			var metaName = _context.ResolveMetadataMemberOrThrow(nameof(EsqlMetadata.Source));
			_projections.Add(metaName == resultField
				? new ProjectionEntry(ProjectionKind.Keep, metaName, metaName, null)
				: new ProjectionEntry(ProjectionKind.Rename, resultField, metaName, null));
			return;
		}

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
			// the same as for a guarded child: with the guard dropped, a missing value
			// comes back as the member's default, which is the guard's null only for a
			// member that can hold it
			if (!targetCanHoldNull)
			{
				throw new NotSupportedException(
					$"A null guard around {targetName} cannot be translated: the member is not declared "
					+ "nullable, so the null the guard produces for a missing value has no way to reach "
					+ "the materialized row. Declare it nullable, without an initializer.");
			}

			ClassifyProjectionMember(resultField, nonNullBranch, targetCanHoldNull, targetName);
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
				var nestedResultField = BuildNestedResultField(resultField, member, newExpression.Type);
				ClassifyProjectionMember(nestedResultField, newExpression.Arguments[i], CanHoldNull(newExpression.Members[i]), newExpression.Members[i].Name);
			}

			return true;
		}

		if (sourceExpression is MemberInitExpression memberInitExpression)
		{
			foreach (var binding in memberInitExpression.Bindings)
			{
				if (binding is not MemberAssignment assignment)
					continue;

				var nestedResultField = BuildNestedResultField(resultField, assignment.Member, memberInitExpression.Type);
				ClassifyProjectionMember(nestedResultField, assignment.Expression, CanHoldNull(assignment.Member), assignment.Member.Name);
			}

			return true;
		}

		return false;
	}

	private string BuildNestedResultField(string resultFieldPrefix, MemberInfo member, Type fallbackDeclaringType)
	{
		var declaringType = member.DeclaringType ?? fallbackDeclaringType;
		var childField = _context.ResolveFieldName(declaringType, member);
		return $"{resultFieldPrefix}.{childField}";
	}

	/// <summary>
	/// Detects null-guard ternary patterns like <c>param == null ? null : param.Field</c>
	/// or <c>param != null ? param.Field : null</c> where one side of the test is a
	/// <see cref="ParameterExpression"/> compared to null, and one branch is null/default.
	/// </summary>
	private bool TryUnwrapNullGuard(ConditionalExpression conditional, out Expression nonNullBranch) =>
		TryUnwrapNullGuard(conditional, out nonNullBranch, out _);

	private bool TryUnwrapNullGuard(ConditionalExpression conditional, out Expression nonNullBranch, out Expression guardedPath)
	{
		guardedPath = null!;
		nonNullBranch = null!;

		if (conditional.Test is not BinaryExpression
			{
				NodeType: ExpressionType.Equal or ExpressionType.NotEqual
			} test)
			return false;

		var left = StripNullableConvert(test.Left);
		var right = StripNullableConvert(test.Right);

		// the guarded side is either the lambda parameter itself, or a member path
		// rooted in it: "param == null" and "param.Child == null" are both guards
		var guarded = IsParameterRooted(left) && IsNullConstant(right) ? left
			: IsParameterRooted(right) && IsNullConstant(left) ? right
			: null;

		if (guarded is null)
			return false;

		guardedPath = guarded;

		// the branch the guard protects: the one that is not the null literal
		var branch = test.NodeType == ExpressionType.Equal
			? IsNullConstant(StripNullableConvert(conditional.IfTrue))
				? StripNullableConvert(conditional.IfFalse)
				: null
			: IsNullConstant(StripNullableConvert(conditional.IfFalse))
				? StripNullableConvert(conditional.IfTrue)
				: null;

		if (branch is null)
			return false;

		// the guard only stands for the branch when the branch reads through the very
		// path that was tested: "p.Supplier == null ? null : p.Name" keeps its own
		// condition, or the emitted CASE would test the wrong field. The document row is
		// never null, so a guard on the bare parameter is only a guard once a projection
		// has made the parameter stand for a value that may be: then it is held to the
		// same rule as a member path.
		if ((guarded is MemberExpression || _context.HasProjected) && !ReadsThrough(branch, guarded))
			return false;

		nonNullBranch = branch;
		return true;
	}

	/// <summary>
	/// Whether a member can hold the null a dropped guard produces: one declared as a
	/// nullable reference, or one of an anonymous type, which has no initializer to
	/// keep. A member declared non-nullable would come back as its default instead.
	/// </summary>
	private static bool CanHoldNull(MemberInfo member) =>
		(member.DeclaringType is { } declaring && declaring.IsDefined(typeof(CompilerGeneratedAttribute), false))
		|| WhereClauseVisitor.IsDeclaredNullable(member);

	/// <summary>The same for a constructor parameter, which stands for the member it initializes.</summary>
	private static bool CanHoldNull(ParameterInfo parameter) => WhereClauseVisitor.IsDeclaredNullable(parameter);

	/// <summary>Whether every member path in the expression goes through <paramref name="path"/>.</summary>
	private bool ReadsThrough(Expression expression, Expression path)
	{
		if (SameMemberPath(expression, path))
			return true;

		return expression switch
		{
			MemberExpression member => member.Expression is not null && ReadsThrough(member.Expression, path),
			UnaryExpression unary => ReadsThrough(unary.Operand, path),
			// a nested init reads through the path only when every member does: a constant
			// member would be emitted for a missing parent, where the source gives null,
			// and a child made of constants alone has nothing that reads through at all.
			// A binding that is not an assignment, such as a nested initializer without
			// new, is not read into, so it does not read through either; and a constructor
			// that takes arguments is not read into by the nested projection at all, which
			// emits the bindings alone, so such a child is not unwrapped rather than
			// unwrapped and then emitted without part of itself.
			MemberInitExpression init => init.Bindings.Count > 0
				&& init.NewExpression.Arguments.Count == 0
				&& init.Bindings.All(b => b is MemberAssignment assignment && ReadsThrough(assignment.Expression, path)),
			// the same for a child built with new, anonymous or by constructor: every
			// argument has to read through the path
			NewExpression construction => construction.Arguments.Count > 0
				&& construction.Arguments.All(argument => ReadsThrough(argument, path)),
			// a guarded child of this child, the shape a selection two levels deep takes:
			// it is null whenever its own guarded path is, and that path goes through this
			// one, so it reads through as well
			ConditionalExpression nested => TryUnwrapNullGuard(nested, out _, out var nestedPath)
				&& nestedPath is MemberExpression
				&& ReadsThrough(nestedPath, path),
			// a call reads through the path when its receiver or one of its arguments does,
			// provided the function is null over a null input: every scalar function is,
			// except the few that exist to answer null, which would give a missing parent
			// a value
			MethodCallExpression call => EsqlFunctionTranslator.PropagatesNull(call)
				&& ((call.Object is not null && ReadsThrough(call.Object, path))
					|| call.Arguments.Any(argument => ReadsThrough(argument, path) || ReadsThroughParams(argument, path))),
			_ => false
		};
	}

	/// <summary>
	/// The values of a params argument arrive in an array of their own, as in
	/// Concat(a, b): one of them reading through the path is enough, the function being
	/// null over a null input like any other.
	/// </summary>
	private bool ReadsThroughParams(Expression argument, Expression path) =>
		argument is NewArrayExpression array && array.Expressions.Any(element => ReadsThrough(element, path));

	private static bool SameMemberPath(Expression left, Expression right) => (left, right) switch
	{
		(ParameterExpression a, ParameterExpression b) => a == b,
		(MemberExpression a, MemberExpression b) => a.Member == b.Member
			&& a.Expression is not null && b.Expression is not null
			&& SameMemberPath(a.Expression, b.Expression),
		_ => false
	};

	/// <summary>An expression that is the lambda parameter, or a member path rooted in it.</summary>
	private static bool IsParameterRooted(Expression expression) => expression switch
	{
		ParameterExpression => true,
		MemberExpression member => member.Expression is not null && IsParameterRooted(member.Expression),
		_ => false
	};

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
			UnaryExpression { NodeType: ExpressionType.Convert } convert => TranslateConvert(convert),
			MethodCallExpression methodCall => TranslateMethodCall(methodCall),
			ConditionalExpression conditional => TranslateConditional(conditional),
			_ => throw new NotSupportedException($"Expression type {expression.GetType().Name} is not supported in projections.")
		};

	private string TranslateConvert(UnaryExpression convert)
	{
		// Implicit/explicit conversion to a DenseVector<T> from a closure-captured T[] /
		// ReadOnlyMemory<T>. Resolve through the implicit operator and emit as parameter / literal.
		if (TryTranslateVectorConvert(convert, out var vectorLiteral))
			return vectorLiteral;

		return TranslateExpression(convert.Operand);
	}

	private bool TryTranslateVectorConvert(UnaryExpression convert, out string result) =>
		DenseVectorTypeHelper.TryEmitDenseVectorLiteral(convert, _context, out result);

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

		// The fallback below renders the test with the C# operator, and "x == null" is
		// not how ES|QL asks that question, so a conditional testing null is refused
		// unless it was folded away above as a guard over the path it reads.
		if (conditional.Test is BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual } comparison
			&& (IsNullConstant(comparison.Left) || IsNullConstant(comparison.Right)))
		{
			throw new NotSupportedException(
				"A conditional testing null in a projection is only supported when it guards a "
				+ "branch that reads through the tested path, as in "
				+ "\"p.Child == null ? null : new Dto { Field = p.Child.Field }\".");
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
#pragma warning disable IDE0028 // collection-expression suggestion would silently drop the explicit comparer
		private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

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
