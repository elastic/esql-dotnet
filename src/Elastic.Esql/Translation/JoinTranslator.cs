// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Text;

using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Emits LOOKUP JOIN pipelines: detects field name collisions between outer and inner types,
/// preserves colliding outer values via EVAL temp fields, and translates the join result
/// selector projection.
/// </summary>
internal sealed class JoinTranslator(EsqlTranslationContext context, ProjectionCommandEmitter projectionEmitter)
{
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));
	private readonly ProjectionCommandEmitter _projectionEmitter = projectionEmitter ?? throw new ArgumentNullException(nameof(projectionEmitter));

	/// <summary>
	/// Shared join emission: detects field collisions, emits EVAL to preserve outer values,
	/// emits LOOKUP JOIN, and processes the result selector projection.
	/// </summary>
	public void EmitJoin(
		string lookupIndex,
		string onCondition,
		Expression resultSelectorArg,
		string? whereNotNullField = null
	)
	{
		if (resultSelectorArg is not UnaryExpression { Operand: LambdaExpression resultLambda }
			|| resultLambda.Body is ParameterExpression)
		{
			// Identity projection - no collision handling needed (no KEEP to filter temps)
			_context.Commands.Add(new LookupJoinCommand(lookupIndex, onCondition));

			if (whereNotNullField is not null)
				_context.Commands.Add(new WhereCommand($"{whereNotNullField} IS NOT NULL"));

			return;
		}

		var innerType = resultLambda.Parameters[1].Type;
		var remappings = DetectJoinFieldCollisions(resultLambda, innerType);

		if (remappings is not null)
			EmitCollisionEval(remappings);

		_context.Commands.Add(new LookupJoinCommand(lookupIndex, onCondition));

		if (whereNotNullField is not null)
			_context.Commands.Add(new WhereCommand($"{whereNotNullField} IS NOT NULL"));

		var projectionVisitor = new SelectProjectionVisitor(_context);
		var result = remappings is not null
			? projectionVisitor.TranslateJoinProjection(resultLambda, resultLambda.Parameters[0], remappings)
			: projectionVisitor.Translate(resultLambda);

		var innerFieldNames = _context.GetAllFieldNames(innerType);
		_projectionEmitter.Emit(result, innerFieldNames);
	}

	/// <summary>
	/// Detects field name collisions between outer and inner types in a join result selector.
	/// Returns a remapping dictionary (originalField -> tempField) for colliding outer fields,
	/// or null if no collisions exist.
	/// </summary>
	private Dictionary<string, string>? DetectJoinFieldCollisions(LambdaExpression resultSelector, Type innerType)
	{
		var innerFieldNames = _context.GetAllFieldNames(innerType);

		if (innerFieldNames.Count == 0)
			return null;

		var outerParam = resultSelector.Parameters[0];
		var collector = new JoinFieldCollector(_context, outerParam);
		_ = collector.Visit(resultSelector.Body);

		Dictionary<string, string>? remappings = null;
		var usedTempAliases = new HashSet<string>(StringComparer.Ordinal);
		foreach (var outerFieldEntry in collector.OuterFields.OrderBy(kv => kv.Key, StringComparer.Ordinal))
		{
			var outerField = outerFieldEntry.Key;
			var isNestedPath = outerFieldEntry.Value;
			var collisionKey = FindCollisionKey(outerField, isNestedPath, innerFieldNames);
			if (collisionKey is null)
				continue;

#pragma warning disable IDE0028 // collection-expression suggestion would silently drop the explicit comparer
			remappings ??= new Dictionary<string, string>(StringComparer.Ordinal);
#pragma warning restore IDE0028
			if (remappings.ContainsKey(collisionKey))
				continue;

			remappings[collisionKey] = BuildCollisionTempFieldName(collisionKey, usedTempAliases);
		}

		return remappings;
	}

	private static string? FindCollisionKey(string outerField, bool isNestedPath, HashSet<string> innerFieldNames)
	{
		if (innerFieldNames.Contains(outerField))
			return outerField;

		if (isNestedPath)
		{
			for (var idx = outerField.LastIndexOf('.'); idx > 0; idx = outerField.LastIndexOf('.', idx - 1))
			{
				var prefix = outerField[..idx];
				if (innerFieldNames.Contains(prefix))
					return prefix;
			}
		}

		var nestedPrefix = $"{outerField}.";
		return innerFieldNames.Any(name => name.StartsWith(nestedPrefix, StringComparison.Ordinal))
			? outerField
			: null;
	}

	private static string BuildCollisionTempFieldName(string collisionKey, HashSet<string> usedAliases)
	{
		var sanitized = SanitizeFieldName(collisionKey);
		var baseAlias = string.IsNullOrEmpty(sanitized) ? "_esql_outer_field" : $"_esql_outer_{sanitized}";
		var alias = baseAlias;
		var suffix = 1;

		while (!usedAliases.Add(alias))
		{
			alias = $"{baseAlias}_{suffix}";
			suffix++;
		}

		return alias;
	}

	private static string SanitizeFieldName(string fieldName)
	{
		var builder = new StringBuilder(fieldName.Length);
		var previousWasUnderscore = false;

		foreach (var character in fieldName)
		{
			if (char.IsLetterOrDigit(character) || character == '_')
			{
				_ = builder.Append(character);
				previousWasUnderscore = false;
				continue;
			}

			if (previousWasUnderscore)
				continue;

			_ = builder.Append('_');
			previousWasUnderscore = true;
		}

		return builder.ToString().Trim('_');
	}

	/// <summary>
	/// Emits <c>EVAL _esql_outer_x = x</c> for each colliding field to preserve outer values
	/// before the LOOKUP JOIN overwrites them.
	/// </summary>
	private void EmitCollisionEval(Dictionary<string, string> remappings)
	{
		var evalExprs = remappings
			.Select(kv => $"{kv.Value} = {kv.Key}")
			.ToList();
		_context.Commands.Add(new EvalCommand(evalExprs));
	}

	/// <summary>
	/// Walks a join result selector collecting resolved field names accessed from the outer parameter.
	/// </summary>
	private sealed class JoinFieldCollector(
		EsqlTranslationContext context,
		ParameterExpression outerParam
	) : ExpressionVisitor
	{
#pragma warning disable IDE0028 // collection-expression suggestion would silently drop the explicit comparer
		public Dictionary<string, bool> OuterFields { get; } = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

		protected override Expression VisitMember(MemberExpression node)
		{
			if (node.Member.DeclaringType is not null && ExpressionTranslationHelpers.IsRootedInParameter(node, outerParam))
			{
				var fieldName = node.ResolveFieldName(context.Metadata);
				var isNestedPath = node.Expression?.UnwrapConvertExpressions() is MemberExpression;
				OuterFields[fieldName] = OuterFields.TryGetValue(fieldName, out var trackedNested)
					? trackedNested || isNestedPath
					: isNestedPath;
				return node;
			}

			return base.VisitMember(node);
		}
	}
}
