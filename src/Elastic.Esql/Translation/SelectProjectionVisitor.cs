// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Elastic.Esql.Formatting;

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
	private readonly HashSet<string> _referencedFields = [];
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
		public IReadOnlyList<(string Field, string Expression)> EvalExpressions { get; init; } = [];
	}

	/// <summary>
	/// Translates a Select lambda to projection commands.
	/// </summary>
	public ProjectionResult Translate(LambdaExpression lambda) =>
		TranslateCore(() => Visit(lambda.Body));

	/// <summary>
	/// Translates a value computed from the row into the column of <paramref name="member"/>: the projection of a
	/// Select that returns a single value, which has no member of its own to project into.
	/// </summary>
	public ProjectionResult TranslateValue(Expression value, PropertyInfo member)
	{
		var declaringType = member.DeclaringType
			?? throw new ArgumentException("The member has no declaring type.", nameof(member));
		return TranslateCore(() => ClassifyProjectionMember(
			_context.ResolveFieldName(declaringType, member),
			value,
			target: member,
			targetName: member.Name
		));
	}

	/// <summary>
	/// Translates a join result selector lambda to projection commands, applying
	/// outer field remappings so that <c>outer.X</c> references resolve to the
	/// EVAL-preserved temp field instead of the post-join (overwritten) column.
	/// </summary>
	public ProjectionResult TranslateJoinProjection(
		LambdaExpression lambda,
		ParameterExpression outerParam,
		Dictionary<string, string>? outerFieldRemappings
	)
	{
		_outerParameter = outerParam;
		_outerFieldRemappings = outerFieldRemappings;
		try
		{
			return TranslateCore(() => Visit(lambda.Body));
		}
		finally
		{
			_outerParameter = null;
			_outerFieldRemappings = null;
		}
	}

	private ProjectionResult TranslateCore(Action classifyMembers)
	{
		_projections.Clear();
		_activeRenames = [];
		_referencedFields.Clear();

		// Pass 1: classify all projection members
		classifyMembers();

		var keepFields = new List<string>();
		var aliases = new List<(string Source, string Target)>();
		foreach (var entry in _projections)
		{
			switch (entry.Kind)
			{
				case ProjectionKind.Keep:
					keepFields.Add(entry.SourceField!);
					break;
				case ProjectionKind.Rename:
					aliases.Add((entry.SourceField!, entry.ResultField));
					break;
			}
		}

		// Pass 2: translate computed fields against the original column names, which also records
		// every column they read.
		var evalExpressions = TranslateEvalExpressions();

		// An alias stays a RENAME only when its source may disappear: the source is not kept, not
		// aliased again, and the target is not a column a computed field reads (RENAME runs before
		// EVAL and would overwrite it). Every other alias becomes an EVAL copy placed after the
		// computed fields, so the source column stays readable under its own name.
		var keptSources = new HashSet<string>(keepFields, StringComparer.Ordinal);
		var sourceUses = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var (source, _) in aliases)
			sourceUses[source] = sourceUses.TryGetValue(source, out var uses) ? uses + 1 : 1;

		var renameFields = new List<(string, string)>();
		var aliasCopies = new List<(string, string)>();
		foreach (var (source, target) in aliases)
		{
			if (keptSources.Contains(source) || sourceUses[source] > 1 || _referencedFields.Contains(target))
				aliasCopies.Add((target, source));
			else
				renameFields.Add((source, target));
		}

		// Pass 3, only when a computed field reads a renamed source: the RENAME has removed that
		// column by the time the EVAL runs, so the computation must read the alias instead.
		if (renameFields.Any(rename => _referencedFields.Contains(rename.Item1)))
		{
			foreach (var (source, target) in renameFields)
				_activeRenames[source] = target;

			evalExpressions = TranslateEvalExpressions();
		}

		evalExpressions.AddRange(aliasCopies);

		return new ProjectionResult
		{
			KeepFields = keepFields,
			RenameFields = renameFields,
			EvalExpressions = evalExpressions
		};
	}

	private List<(string, string)> TranslateEvalExpressions()
	{
		var evalExpressions = new List<(string, string)>();
		foreach (var entry in _projections)
		{
			if (entry.Kind == ProjectionKind.Eval)
				evalExpressions.Add((entry.ResultField, TranslateExpression(entry.SourceExpression!)));
		}

		return evalExpressions;
	}

	protected override Expression VisitNew(NewExpression node)
	{
		if (node.Members is not null)
		{
			var isAnonymous = node.Type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
			HashSet<string>? anonymousFieldNames = isAnonymous ? new(StringComparer.Ordinal) : null;

			for (var i = 0; i < node.Arguments.Count; i++)
			{
				var arg = node.Arguments[i];
				var member = node.Members[i];

				var declaringType = member.DeclaringType ?? node.Type;
				var resultField = _context.ResolveFieldName(declaringType, member);
				_ = anonymousFieldNames?.Add(resultField);

				ClassifyProjectionMember(resultField, arg, target: member, targetName: member.Name);
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
			ClassifyProjectionMember(
				EsqlIdentifier.EscapeColumnName(jsonProp.Name),
				node.Arguments[i],
				target: parameters[i],
				targetName: paramName);
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
				ClassifyProjectionMember(
					resultField,
					assignment.Expression,
					target: assignment.Member,
					targetName: assignment.Member.Name);
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

	private void ClassifyProjectionMember(string resultField, Expression sourceExpression, ICustomAttributeProvider target, string? targetName)
	{
		// A null-guarded nested projection, the shape a GraphQL layer emits for
		// "parent { child }": param == null ? null : new Child { Field = param.Child.Field }
		if (sourceExpression is ConditionalExpression guarded
			&& TryUnwrapNullGuard(guarded, out var guardedBranch, out var guardedChildPath, propagatingNull: true)
			&& guardedBranch is MemberInitExpression or NewExpression)
		{
			// Dropping the guard leaves no column to say the child is missing: a missing
			// parent comes back as whatever the member holds by default, which is null
			// only for a member declared nullable. Anywhere else the null the guard
			// produces has no way to reach the row, so the shape is refused. A guard on
			// the row parameter is not one of those cases: the document is never null, so
			// the guard is redundant rather than meaningful, and dropping it changes
			// nothing about what reaches the row. The lookup side of a join is null for a
			// row with no match, so a guard there is meaningful like any other.
			if (!IsRowThatIsNeverNull(guardedChildPath) && !CanHoldNull(target))
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
			// the cast says nothing about the member: the target keeps its own nullability
			ClassifyProjectionMember(resultField, unary.Operand, target: target, targetName: targetName);
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
			&& TryUnwrapNullGuard(conditional, out var nonNullBranch, out var guardedValuePath, propagatingNull: true)
			&& IsSimpleFieldAccess(nonNullBranch))
		{
			// A scalar needs no refusal here: the column is null for a missing value either
			// way, and the member keeps whatever it holds, exactly as the unguarded
			// "Message = l.Host!.Name" does. Only a child object differs, where null and
			// an empty object are not the same thing.

			ClassifyProjectionMember(resultField, nonNullBranch, target: target, targetName: targetName);
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
				ClassifyProjectionMember(
					nestedResultField,
					newExpression.Arguments[i],
					target: newExpression.Members[i],
					targetName: newExpression.Members[i].Name);
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
				ClassifyProjectionMember(
					nestedResultField,
					assignment.Expression,
					target: assignment.Member,
					targetName: assignment.Member.Name);
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
	/// Detects a null guard, <c>path == null ? null : branch</c> or
	/// <c>path != null ? branch : null</c>, where the tested path is the lambda parameter
	/// or a member path rooted in it and one branch is the null literal.
	/// <para>
	/// A guard over a member path, and one over the parameter once it stands for a value
	/// that may be null, holds only when the branch reads through the very path that was
	/// tested: otherwise the guard says nothing about what the branch reads, and dropping
	/// it would give a missing parent a value.
	/// </para>
	/// <para>
	/// With <paramref name="propagatingNull"/> the branch has to carry a null of the path
	/// through as well, since the guard is dropped and the null it produced has to come
	/// from the branch. Without it the branch has only to read the path, which is enough
	/// for the CASE fold: that keeps the test, as IS NOT NULL over the branch's columns.
	/// </para>
	/// </summary>
	private bool TryUnwrapNullGuard(
		ConditionalExpression conditional,
		out Expression nonNullBranch,
		out Expression guardedPath,
		bool propagatingNull)
	{
		guardedPath = null!;
		nonNullBranch = null!;

		if (conditional.Test is not BinaryExpression
			{
				NodeType: ExpressionType.Equal or ExpressionType.NotEqual
			} test)
			return false;

		// a hand-built tree types Expression.Constant(null) by wrapping it in a Convert,
		// so both sides are unwrapped rather than only the Nullable<T> casts
		var left = test.Left.UnwrapConvertExpressions();
		var right = test.Right.UnwrapConvertExpressions();

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
			? IsNullConstant(conditional.IfTrue.UnwrapConvertExpressions())
				? StripNullableConvert(conditional.IfFalse)
				: null
			: IsNullConstant(conditional.IfFalse.UnwrapConvertExpressions())
				? StripNullableConvert(conditional.IfTrue)
				: null;

		if (branch is null)
			return false;

		// the guard only stands for the branch when the branch reads through the very
		// path that was tested: "p.Supplier == null ? null : p.Name" keeps its own
		// condition, or the emitted CASE would test the wrong field. The document row is
		// never null, so a guard on the bare parameter is only a guard once the parameter
		// stands for a value that may be: after a projection, or on the lookup side of a
		// join, where a row with no match leaves it null. Then it is held to the same rule
		// as a member path.
		if ((guarded is MemberExpression || _context.HasProjected || IsJoinLookupParameter(guarded))
			&& !ReadsThrough(branch, guarded, propagatingNull))
			return false;

		nonNullBranch = branch;
		return true;
	}

	/// <summary>
	/// Whether the guarded path is the row the query started from, which is never null:
	/// a guard on it is redundant rather than meaningful. After a projection the parameter
	/// stands for whatever the selector built, which can be null, and on the lookup side of
	/// a join a row with no match leaves it null, so neither is that row.
	/// </summary>
	private bool IsRowThatIsNeverNull(Expression guardedPath) =>
		guardedPath is ParameterExpression
		&& !_context.HasProjected
		&& !IsJoinLookupParameter(guardedPath);

	/// <summary>
	/// Whether the expression is the lookup-side parameter of a join result selector: a
	/// row with no match leaves it null, so a guard on it says something, unlike one on
	/// the document row.
	/// </summary>
	private bool IsJoinLookupParameter(Expression expression) =>
		_outerParameter is not null
		&& expression is ParameterExpression parameter
		&& parameter != _outerParameter;

	/// <summary>
	/// Whether the target of a projection member can hold the null a dropped guard
	/// produces: any except one declared non-nullable, which would come back as its
	/// default instead. An anonymous type's properties carry no annotation, so they can.
	/// The target is the member, or the constructor parameter that stands for one.
	/// </summary>
	private static bool CanHoldNull(ICustomAttributeProvider target) => target switch
	{
		MemberInfo member => WhereClauseVisitor.IsDeclaredNullable(member),
		ParameterInfo parameter => WhereClauseVisitor.IsDeclaredNullable(parameter),
		_ => throw new NotSupportedException($"A projection member cannot be a {target.GetType().Name}.")
	};

	/// <summary>
	/// Whether every member path in the expression goes through <paramref name="path"/>.
	/// With <paramref name="propagatingNull"/> it has to go through operations that are
	/// null over a null input, so that a null of the path reaches the result.
	/// </summary>
	private bool ReadsThrough(Expression expression, Expression path, bool propagatingNull)
	{
		if (SameMemberPath(expression, path))
			return true;

		return expression switch
		{
			MemberExpression member => member.Expression is not null && ReadsThrough(member.Expression, path, propagatingNull),
			UnaryExpression unary => ReadsThrough(unary.Operand, path, propagatingNull),
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
				&& init.Bindings.All(b => b is MemberAssignment assignment && ReadsThrough(assignment.Expression, path, propagatingNull)),
			// arithmetic and comparison are null over a null operand, like the functions
			// below, so a side that reads through the path carries the null through. The
			// operators that answer over a null do not: ??, the short-circuiting pair, and a
			// comparison with null, which renders as IS NULL or IS NOT NULL.
			BinaryExpression binary when !propagatingNull || PropagatesNull(binary) =>
				ReadsThrough(binary.Left, path, propagatingNull) || ReadsThrough(binary.Right, path, propagatingNull),
			// the same for a child built with new, anonymous or by constructor: every
			// argument has to read through the path, and only a child the nested projection
			// can emit, since it reads the bindings, so a child built by constructor has
			// nothing for it to read and would be unwrapped into a shape that fails later
			NewExpression construction => construction.Members is not null
				&& construction.Arguments.Count > 0
				&& construction.Arguments.All(argument => ReadsThrough(argument, path, propagatingNull)),
			// a guarded child of this child, the shape a selection two levels deep takes:
			// it is null whenever its own guarded path is, and that path goes through this
			// one, so it reads through as well
			ConditionalExpression nested => TryUnwrapNullGuard(nested, out _, out var nestedPath, propagatingNull)
				&& nestedPath is MemberExpression
				&& ReadsThrough(nestedPath, path, propagatingNull),
			// a call reads through the path when its receiver or one of its arguments does,
			// provided the function is null over a null input: every scalar function is,
			// except the few that exist to answer null, which would give a missing parent
			// a value
			MethodCallExpression call when !propagatingNull || EsqlFunctionTranslator.PropagatesNull(call) =>
				(call.Object is not null && ReadsThrough(call.Object, path, propagatingNull))
				|| call.Arguments.Any(argument => ReadsThrough(argument, path, propagatingNull)
					|| ReadsThroughParams(argument, path, propagatingNull)),
			_ => false
		};
	}

	/// <summary>
	/// The values of a params argument arrive in an array of their own, as in
	/// Concat(a, b): one of them reading through the path is enough, the function being
	/// null over a null input like any other.
	/// </summary>
	private bool ReadsThroughParams(Expression argument, Expression path, bool propagatingNull) =>
		argument is NewArrayExpression array && array.Expressions.Any(element => ReadsThrough(element, path, propagatingNull));

	/// <summary>
	/// Whether the operator is null over a null operand: every one except ??, the
	/// short-circuiting pair, and a comparison with null, which renders as IS NULL or
	/// IS NOT NULL and answers a null with a boolean.
	/// </summary>
	private static bool PropagatesNull(BinaryExpression binary) =>
		binary.NodeType is not (ExpressionType.Coalesce or ExpressionType.AndAlso or ExpressionType.OrElse)
		&& !IsNullComparison(binary);

	private static bool IsNullComparison(BinaryExpression binary) =>
		binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
		&& (IsNullConstant(binary.Left.UnwrapConvertExpressions()) || IsNullConstant(binary.Right.UnwrapConvertExpressions()));

	/// <summary>
	/// The operation through which the expression reads <paramref name="path"/> while
	/// answering a null input with a value of its own, named for a refusal, or null when
	/// there is none: a call not marked as null over a null input, ??, the
	/// short-circuiting pair, or a comparison with null.
	/// </summary>
	private string? FindAnswerOverNull(Expression expression, Expression path)
	{
		var finder = new AnswerOverNullFinder(candidate => ReadsThrough(candidate, path, propagatingNull: false));
		_ = finder.Visit(expression);
		return finder.Found;
	}

	private sealed class AnswerOverNullFinder(Func<Expression, bool> readsPath) : ExpressionVisitor
	{
		public string? Found { get; private set; }

		public override Expression? Visit(Expression? node)
		{
			if (Found is not null || node is null)
				return node;

			Found = node switch
			{
				MethodCallExpression call when !EsqlFunctionTranslator.PropagatesNull(call) && readsPath(call) =>
					$"\"{call.Method.Name}\"",
				BinaryExpression binary when !PropagatesNull(binary) && readsPath(binary) => binary.NodeType switch
				{
					ExpressionType.Coalesce => "\"??\"",
					ExpressionType.AndAlso => "\"&&\"",
					ExpressionType.OrElse => "\"||\"",
					ExpressionType.Equal => "\"== null\"",
					_ => "\"!= null\""
				},
				_ => null
			};

			return Found is null ? base.Visit(node) : node;
		}
	}

	private static bool SameMemberPath(Expression left, Expression right) =>
		(left.UnwrapConvertExpressions(), right.UnwrapConvertExpressions()) switch
		{
			(ParameterExpression a, ParameterExpression b) => a == b,
			(MemberExpression a, MemberExpression b) => a.Member == b.Member
				&& a.Expression is not null && b.Expression is not null
				&& SameMemberPath(a.Expression, b.Expression),
			_ => false
		};

	/// <summary>
	/// An expression that is the lambda parameter, or a member path rooted in it. The
	/// path is walked by <see cref="ExpressionTranslationHelpers.IsRootedInParameter"/>,
	/// which unwraps the conversions a cast leaves on the chain.
	/// </summary>
	private static bool IsParameterRooted(Expression expression) => expression switch
	{
		ParameterExpression => true,
		MemberExpression member => ExpressionTranslationHelpers.IsRootedInParameter(member),
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
				return EsqlFunctionTranslator.TryTranslateStaticDateProperty(memberName)
					?? throw new NotSupportedException($"DateTime property {memberName} is not supported in projections.");
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
			return EsqlFunctionTranslator.TryTranslateDateMember(memberName, dateExpr)
				?? throw new NotSupportedException($"DateTime property {memberName} is not supported in projections.");
		}

		if (declaringType == typeof(string) && memberName == "Length")
		{
			var strExpr = TranslateExpression(member.Expression!);
			return $"LENGTH({strExpr})";
		}

		var fieldName = member.ResolveFieldName(_context.Metadata);
		fieldName = ApplyOuterRemapping(member, fieldName);
		_ = _referencedFields.Add(fieldName);
		return _activeRenames.TryGetValue(fieldName, out var renamed) ? renamed : fieldName;
	}

	private string TranslateBinary(BinaryExpression binary)
	{
		var dayOfWeekComparison = EsqlFunctionTranslator.TryGetDayOfWeekComparison(binary);
		if (dayOfWeekComparison.HasValue)
		{
			var dateMember = TranslateExpression(dayOfWeekComparison.Value.DateMember);
			return $"({dateMember} {EsqlFunctionTranslator.GetOperator(binary.NodeType)} {dayOfWeekComparison.Value.IsoDayNumber})";
		}

		// "x == null" is IS NULL in ES|QL, where the C# operator answers null for every
		// row, as the where clause already renders it
		if (binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
		{
			var nullOperator = binary.NodeType == ExpressionType.Equal ? "IS NULL" : "IS NOT NULL";

			if (IsNullConstant(binary.Right.UnwrapConvertExpressions()))
				return $"({TranslateExpression(binary.Left)} {nullOperator})";

			if (IsNullConstant(binary.Left.UnwrapConvertExpressions()))
				return $"({TranslateExpression(binary.Right)} {nullOperator})";
		}

		var left = TranslateExpression(binary.Left);
		var right = TranslateExpression(binary.Right);
		var op = EsqlFunctionTranslator.GetOperator(binary.NodeType);

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
				"get_Chars" => EsqlFunctionTranslator.TranslateStringIndexer(target, methodCall.Arguments[0], TranslateExpression),
				_ => throw new NotSupportedException($"String method {methodName} is not supported in projections.")
			};
		}

		throw new NotSupportedException($"Method {declaringType?.Name}.{methodName} is not supported in projections.");
	}

	private string TranslateConditional(ConditionalExpression conditional)
	{
		// The fold keeps the guard, as IS NOT NULL over the branch's columns, so the branch
		// has only to read the guarded path, not to carry its null through. A child object
		// is not folded: it is projected by the nested projection or refused below.
		if (TryUnwrapNullGuard(conditional, out var nonNullBranch, out _, propagatingNull: false)
			&& nonNullBranch is not (MemberInitExpression or NewExpression))
		{
			var nullCheckFields = ExtractNullCheckFields(nonNullBranch);
			if (nullCheckFields.Count > 0)
			{
				var nullCheck = string.Join(" AND ", nullCheckFields.Select(f => $"{f} IS NOT NULL"));
				var expr = TranslateExpression(nonNullBranch);
				return $"CASE WHEN {nullCheck} THEN {expr} ELSE NULL END";
			}
		}

		// A guard over a path of the row that is not folded cannot be emitted either: the
		// CASE below would test the guarded path and answer with a branch that does not
		// depend on it, giving a missing parent a value. The branch either reads elsewhere,
		// or reads the path through an operation that answers a null with a value of its
		// own, and the refusal says which. A conditional that tests null without guarding a
		// path of the row, as in "l.Tag == null ? \"none\" : l.Tag", is a plain CASE and is
		// emitted, now that the test renders as IS NULL.
		if (conditional.Test is BinaryExpression comparison && IsNullComparison(comparison))
		{
			var guarded = IsParameterRooted(comparison.Left.UnwrapConvertExpressions()) ? comparison.Left
				: IsParameterRooted(comparison.Right.UnwrapConvertExpressions()) ? comparison.Right
				: null;

			var branch = IsNullConstant(StripNullableConvert(conditional.IfTrue)) ? StripNullableConvert(conditional.IfFalse)
				: IsNullConstant(StripNullableConvert(conditional.IfFalse)) ? StripNullableConvert(conditional.IfTrue)
				: null;

			if (guarded is not null && branch is not null)
			{
				var answersOverNull = FindAnswerOverNull(branch, guarded);

				throw new NotSupportedException(answersOverNull is null
					? "A null guard in a projection is only supported when the branch it guards reads "
						+ "through the tested path, as in "
						+ "\"p.Child == null ? null : new Dto { Field = p.Child.Field }\"."
					: $"A null guard in a projection cannot be dropped around {answersOverNull}: it answers "
						+ "a null input with a value of its own, so a missing parent would come back with "
						+ "that value rather than null.");
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
}
