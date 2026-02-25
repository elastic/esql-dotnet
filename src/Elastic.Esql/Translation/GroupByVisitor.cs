// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.Formatting;
using Elastic.Esql.Functions;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ GroupBy expressions to ES|QL STATS...BY commands.
/// </summary>
internal sealed class GroupByVisitor(EsqlTranslationContext context) : ExpressionVisitor
{
	private const string SingleKeyMarker = "__single_key__";
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));

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
			case MemberExpression:
				fields.Add(expression.ResolveFieldName(_context.FieldMetadataResolver));
				break;

			case NewExpression newExpr when newExpr.Members != null:
				// Multiple grouping keys: new { x.A, x.B }
				foreach (var arg in newExpr.Arguments)
				{
					if (arg is MethodCallExpression methodArg && methodArg.Method.DeclaringType == typeof(EsqlFunctions))
						fields.Add(TranslateGroupingFunction(methodArg));
					else
						fields.Add(arg.ResolveFieldName(_context.FieldMetadataResolver));
				}

				break;

			case UnaryExpression { NodeType: ExpressionType.Convert, Operand: var operand }:
				fields.AddRange(ExtractGroupByFields(operand));
				break;

			case ConstantExpression:
				// Grouping by constant (like 1) means no grouping fields - just aggregate all
				break;

			case MethodCallExpression methodCall when methodCall.Method.DeclaringType == typeof(GeneralPurposeExtensions):
				fields.Add(methodCall.ResolveFieldName(_context.FieldMetadataResolver));
				break;

			case MethodCallExpression methodCall:
				// Grouping by function: EsqlFunctions.Bucket(l.Duration, 10)
				fields.Add(TranslateGroupingFunction(methodCall));
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

		var members = ExtractResultMembers(resultSelector.Body);

		foreach (var (memberName, arg) in members)
		{
			var agg = TryExtractAggregation(arg, memberName);
			if (agg != null)
				aggregations.Add(agg);
			else if (TryGetKeyPropertyName(arg, out var keyPropName))
				keyAliasMap[keyPropName] = memberName;
		}

		// If no aggregations found, default to count
		if (aggregations.Count == 0)
			aggregations.Add("count = COUNT(*)");

		return (aggregations, keyAliasMap);
	}

	private List<(string memberName, Expression argument)> ExtractResultMembers(Expression body)
	{
		if (body is MemberInitExpression initExpr)
			return ExtractFromMemberInit(initExpr);

		if (body is NewExpression { Members: not null } newExpr)
			return ExtractFromNewExpression(newExpr);

		return [];
	}

	private List<(string memberName, Expression argument)> ExtractFromNewExpression(NewExpression newExpr)
	{
		var resolver = _context.FieldMetadataResolver;
		var isAnonymous = newExpr.Type.IsDefined(typeof(CompilerGeneratedAttribute), false);
		var result = new List<(string, Expression)>(newExpr.Arguments.Count);

		for (var i = 0; i < newExpr.Arguments.Count; i++)
		{
			var member = newExpr.Members![i];
			var name = isAnonymous
				? resolver.GetAnonymousFieldName(member.Name)
				: resolver.GetFieldName(member.DeclaringType!, member);

			result.Add((name, newExpr.Arguments[i]));
		}

		return result;
	}

	private List<(string memberName, Expression argument)> ExtractFromMemberInit(MemberInitExpression initExpr)
	{
		var resolver = _context.FieldMetadataResolver;
		var result = new List<(string, Expression)>(initExpr.Bindings.Count);

		foreach (var binding in initExpr.Bindings)
		{
			if (binding is MemberAssignment assignment)
			{
				var name = resolver.GetFieldName(assignment.Member.DeclaringType!, assignment.Member);
				result.Add((name, assignment.Expression));
			}
		}

		return result;
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
		// Unwrap convert expression, only if it is used to change nullability.
		if (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert && IsNullabilityChange(convert))
			expression = convert.Operand;

		if (expression is not MethodCallExpression methodCall)
			return null;

		var methodName = methodCall.Method.Name;
		var declaringType = methodCall.Method.DeclaringType;

		// Check for EsqlFunctions aggregation methods
		if (declaringType == typeof(EsqlFunctions))
			return TryExtractEsqlAggregation(methodCall, methodName, resultName);

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

#if NET8_0_OR_GREATER
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Expression compilation fallback for aggregation constant arguments.")]
#endif
	private string? TryExtractEsqlAggregation(MethodCallExpression methodCall, string methodName, string resultName)
	{
		// EsqlFunctions aggregation methods follow the pattern:
		// EsqlFunctions.CountDistinct(g, l => l.Field)
		// First arg is the source (group), second is the field selector, optional third+ are extra args

		string ExtractField(int selectorIndex)
		{
			if (methodCall.Arguments.Count > selectorIndex)
				return ExtractFieldFromSelector(methodCall.Arguments[selectorIndex]);
			return "*";
		}

		string? ExtractConstantArg(int index)
		{
			if (methodCall.Arguments.Count <= index)
				return null;
			var arg = methodCall.Arguments[index];
			var value = arg switch
			{
				ConstantExpression c => c.Value,
				UnaryExpression { NodeType: ExpressionType.Convert, Operand: ConstantExpression c2 } => c2.Value,
				MemberExpression member when member.Expression is ConstantExpression ce =>
					member.Member switch
					{
						System.Reflection.FieldInfo fi => fi.GetValue(ce.Value),
						System.Reflection.PropertyInfo pi => pi.GetValue(ce.Value),
						_ => null
					},
				_ => Expression.Lambda(arg).Compile().DynamicInvoke()
			};
			return value?.ToString();
		}

		var fieldExpr = ExtractField(1);

		return methodName switch
		{
			"CountDistinct" => $"{resultName} = COUNT_DISTINCT({fieldExpr})",
			"Median" => $"{resultName} = MEDIAN({fieldExpr})",
			"MedianAbsoluteDeviation" => $"{resultName} = MEDIAN_ABSOLUTE_DEVIATION({fieldExpr})",
			"Percentile" => $"{resultName} = PERCENTILE({fieldExpr}, {ExtractConstantArg(2)})",
			"StdDev" => $"{resultName} = STD_DEV({fieldExpr})",
			"Variance" => $"{resultName} = VARIANCE({fieldExpr})",
			"WeightedAvg" => $"{resultName} = WEIGHTED_AVG({fieldExpr}, {ExtractField(2)})",
			"Top" => $"{resultName} = TOP({fieldExpr}, {ExtractConstantArg(2)}, {EsqlFormatting.FormatValue(ExtractConstantArg(3))})",
			"Values" => $"{resultName} = VALUES({fieldExpr})",
			"First" => $"{resultName} = FIRST({fieldExpr})",
			"Last" => $"{resultName} = LAST({fieldExpr})",
			"Sample" => $"{resultName} = SAMPLE({fieldExpr})",
			"Absent" => $"{resultName} = ABSENT({fieldExpr})",
			"Present" => $"{resultName} = PRESENT({fieldExpr})",
			_ => null
		};
	}

#if NET8_0_OR_GREATER
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Expression compilation fallback for grouping function arguments.")]
#endif
	private string TranslateGroupingFunction(MethodCallExpression methodCall)
	{
		var declaringType = methodCall.Method.DeclaringType;
		if (declaringType != typeof(EsqlFunctions))
			throw new NotSupportedException($"Only EsqlFunctions grouping methods are supported, got {declaringType?.Name}.{methodCall.Method.Name}.");

		var methodName = methodCall.Method.Name;

		string Translate(Expression e)
		{
			try
			{
				return e.ResolveFieldName(_context.FieldMetadataResolver);
			}
			catch (NotSupportedException)
			{
				if (e is ConstantExpression constant)
					return EsqlFormatting.FormatValue(constant.Value);
				return EsqlFormatting.FormatValue(Expression.Lambda(e).Compile().DynamicInvoke());
			}
		}

		var result = EsqlFunctionTranslator.TryTranslate(methodName, Translate, methodCall.Arguments);
		return result ?? throw new NotSupportedException($"Grouping function {methodName} is not supported.");
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

	private string ExtractFieldFromLambdaBody(Expression body)
	{
		try
		{
			return body.ResolveFieldName(_context.FieldMetadataResolver);
		}
		catch (NotSupportedException)
		{
			return "*";
		}
	}

	private static bool IsNullabilityChange(UnaryExpression convert) =>
		Nullable.GetUnderlyingType(convert.Type) == convert.Operand.Type
		|| Nullable.GetUnderlyingType(convert.Operand.Type) == convert.Type;

	private static bool IsAggregationMethod(string methodName) => methodName is "Count" or "LongCount" or "Sum" or "Average" or "Min" or "Max";
}
