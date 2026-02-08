// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Core;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ GroupBy expressions to ES|QL STATS...BY commands.
/// </summary>
public class GroupByVisitor(EsqlQueryContext context) : ExpressionVisitor
{
	private const string SingleKeyMarker = "__single_key__";
	private readonly EsqlQueryContext _context = context ?? throw new ArgumentNullException(nameof(context));

	/// <summary>
	/// Translates a GroupBy key selector to a STATS command (without result selector).
	/// </summary>
	public StatsCommand Translate(LambdaExpression keySelector)
	{
		var groupByFields = ExtractGroupByFields(keySelector.Body);

		// Default aggregation - Count
		var aggregations = new[] { "count = COUNT(*)" };

		return new StatsCommand(aggregations, groupByFields.Count > 0 ? groupByFields : null);
	}

	/// <summary>
	/// Translates a GroupBy with result selector (from subsequent Select) to a STATS command.
	/// </summary>
	public StatsCommand Translate(LambdaExpression keySelector, LambdaExpression resultSelector)
	{
		var groupByFields = ExtractGroupByFields(keySelector.Body);
		var keyPropertyNames = ExtractKeyPropertyNames(keySelector.Body);
		var (aggregations, keyAliasMap) = ExtractAggregationsAndKeyAliases(resultSelector);

		// Alias BY fields to match the result selector member names so that
		// the ES|QL response column names align with the anonymous type parameters.
		// Without this, dotted fields like "service.name" won't match "service" in the materializer.
		ApplyByFieldAliases(groupByFields, keyPropertyNames, keyAliasMap);

		// If grouping by a constant (like 1), there's no BY clause
		var byFields = groupByFields.Count > 0 ? groupByFields : null;

		return new StatsCommand(aggregations, byFields);
	}

	private List<string> ExtractGroupByFields(Expression expression)
	{
		var fields = new List<string>();

		switch (expression)
		{
			case MemberExpression member:
				fields.Add(_context.MetadataResolver.Resolve(member.Member));
				break;

			case NewExpression newExpr when newExpr.Members != null:
				// Multiple grouping keys: new { x.A, x.B }
				foreach (var arg in newExpr.Arguments)
				{
					if (arg is MemberExpression memberArg)
						fields.Add(_context.MetadataResolver.Resolve(memberArg.Member));
				}

				break;

			case UnaryExpression { NodeType: ExpressionType.Convert, Operand: var operand }:
				fields.AddRange(ExtractGroupByFields(operand));
				break;

			case ConstantExpression:
				// Grouping by constant (like 1) means no grouping fields - just aggregate all
				break;

			default:
				throw new NotSupportedException($"GroupBy key expression type {expression.GetType().Name} is not supported.");
		}

		return fields;
	}

	private (List<string> aggregations, Dictionary<string, string> keyAliasMap) ExtractAggregationsAndKeyAliases(LambdaExpression resultSelector)
	{
		var aggregations = new List<string>();
		var keyAliasMap = new Dictionary<string, string>();

		// The result selector looks like: g => new { Level = g.Key, Count = g.Count() }
		// or for composite keys: g => new { g.Key.Level, g.Key.StatusCode, Count = g.Count() }
		if (resultSelector.Body is NewExpression newExpr && newExpr.Members != null)
		{
			for (var i = 0; i < newExpr.Arguments.Count; i++)
			{
				var arg = newExpr.Arguments[i];
				var memberName = ToCamelCase(newExpr.Members[i].Name);

				var agg = TryExtractAggregation(arg, memberName);
				if (agg != null)
					aggregations.Add(agg);
				else if (TryGetKeyPropertyName(arg, out var keyPropName))
					keyAliasMap[keyPropName] = memberName;
			}
		}

		// If no aggregations found, default to count
		if (aggregations.Count == 0)
			aggregations.Add("count = COUNT(*)");

		return (aggregations, keyAliasMap);
	}

	/// <summary>
	/// Extracts the key property names from a composite GroupBy key selector.
	/// Returns null for single-key GroupBy, or a list of property names for composite keys.
	/// </summary>
	private static List<string>? ExtractKeyPropertyNames(Expression expression) =>
		expression is NewExpression { Members: not null } newExpr
			? newExpr.Members.Select(m => m.Name).ToList()
			: null;

	private static void ApplyByFieldAliases(
		List<string> groupByFields,
		List<string>? keyPropertyNames,
		Dictionary<string, string> keyAliasMap)
	{
		if (keyAliasMap.Count == 0 || groupByFields.Count == 0)
			return;

		if (keyPropertyNames == null)
		{
			// Single key: g.Key → sentinel key in map
			if (keyAliasMap.TryGetValue(SingleKeyMarker, out var alias) && alias != groupByFields[0])
				groupByFields[0] = $"{alias} = {groupByFields[0]}";
		}
		else
		{
			// Composite key: g.Key.PropertyName → property name as key in map
			for (var i = 0; i < groupByFields.Count && i < keyPropertyNames.Count; i++)
			{
				if (keyAliasMap.TryGetValue(keyPropertyNames[i], out var alias) && alias != groupByFields[i])
					groupByFields[i] = $"{alias} = {groupByFields[i]}";
			}
		}
	}

	private static bool TryGetKeyPropertyName(Expression expression, out string keyPropertyName)
	{
		switch (expression)
		{
			// g.Key (single key)
			case MemberExpression { Member.Name: "Key" }:
				keyPropertyName = SingleKeyMarker;
				return true;

			// Convert(g.Key)
			case UnaryExpression { NodeType: ExpressionType.Convert, Operand: MemberExpression { Member.Name: "Key" } }:
				keyPropertyName = SingleKeyMarker;
				return true;

			// g.Key.PropertyName (composite key)
			case MemberExpression member when member.Expression is MemberExpression { Member.Name: "Key" }:
				keyPropertyName = member.Member.Name;
				return true;

			// Convert(g.Key.PropertyName)
			case UnaryExpression { NodeType: ExpressionType.Convert, Operand: MemberExpression composite }
				when composite.Expression is MemberExpression { Member.Name: "Key" }:
				keyPropertyName = composite.Member.Name;
				return true;

			default:
				keyPropertyName = "";
				return false;
		}
	}

	private string? TryExtractAggregation(Expression expression, string resultName)
	{
		if (expression is not MethodCallExpression methodCall)
			return null;

		var methodName = methodCall.Method.Name;

		// LINQ aggregation methods are extension methods:
		// g.Count() → Enumerable.Count(g)
		// g.Sum(x => x.Field) → Enumerable.Sum(g, x => x.Field)

		// Check if this is a LINQ aggregation method
		if (!IsAggregationMethod(methodName))
			return null;

		// Extract the field name if there's a selector lambda
		var fieldExpr = "*";

		// For methods like Sum, Average, Min, Max - the selector is the second argument
		// (first argument is the source IEnumerable/IGrouping)
		if (methodCall.Arguments.Count > 1)
		{
			var selector = methodCall.Arguments[1];
			fieldExpr = ExtractFieldFromSelector(selector);
		}

		return methodName switch
		{
			"Count" => $"{resultName} = COUNT(*)",
			"LongCount" => $"{resultName} = COUNT(*)",
			"Sum" => $"{resultName} = SUM({fieldExpr})",
			"Average" => $"{resultName} = AVG({fieldExpr})",
			"Min" => $"{resultName} = MIN({fieldExpr})",
			"Max" => $"{resultName} = MAX({fieldExpr})",
			_ => null
		};
	}

	private string ExtractFieldFromSelector(Expression selector)
	{
		// Unwrap UnaryExpression (Quote) to get the lambda
		if (selector is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lambda })
			return ExtractFieldFromLambdaBody(lambda.Body);

		if (selector is LambdaExpression directLambda)
			return ExtractFieldFromLambdaBody(directLambda.Body);

		return "*";
	}

	private string ExtractFieldFromLambdaBody(Expression body) =>
		body switch
		{
			MemberExpression member => _context.MetadataResolver.Resolve(member.Member),
			UnaryExpression { NodeType: ExpressionType.Convert, Operand: MemberExpression innerMember } =>
				_context.MetadataResolver.Resolve(innerMember.Member),
			_ => "*"
		};

	private static bool IsAggregationMethod(string methodName) => methodName is "Count" or "LongCount" or "Sum" or "Average" or "Min" or "Max";

	private static string ToCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		if (name.Length == 1)
			return name.ToLowerInvariant();

		return char.ToLowerInvariant(name[0]) + name.Substring(1);
	}
}
