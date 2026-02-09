// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Reflection;
using Elastic.Esql.Core;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ OrderBy expressions to ES|QL SORT commands.
/// </summary>
public class OrderByVisitor(EsqlQueryContext context) : ExpressionVisitor
{
	private readonly EsqlQueryContext _context = context ?? throw new ArgumentNullException(nameof(context));

	/// <summary>
	/// Translates an OrderBy key selector to a SortField.
	/// </summary>
	public SortField Translate(LambdaExpression lambda, bool descending)
	{
		var fieldName = ExtractFieldName(lambda.Body);
		return new SortField(fieldName, descending);
	}

	private string ExtractFieldName(Expression expression) =>
		expression switch
		{
			MemberExpression member => ResolveWithKeyword(member.Member),
			UnaryExpression { NodeType: ExpressionType.Convert, Operand: MemberExpression innerMember } =>
				ResolveWithKeyword(innerMember.Member),
			MethodCallExpression methodCall => TranslateMethodCall(methodCall),
			_ => throw new NotSupportedException($"Cannot extract field name from expression: {expression}")
		};

	private string ResolveWithKeyword(MemberInfo member)
	{
		var fieldName = _context.MetadataResolver.Resolve(member);
		if (_context.MetadataResolver.IsTextField(member))
			fieldName += ".keyword";
		return fieldName;
	}

	private string TranslateMethodCall(MethodCallExpression methodCall)
	{
		var methodName = methodCall.Method.Name;
		var declaringType = methodCall.Method.DeclaringType;

		// String methods that can be used for sorting
		if (declaringType == typeof(string) && methodCall.Object is MemberExpression member)
		{
			var fieldName = ResolveWithKeyword(member.Member);

			return methodName switch
			{
				"ToLower" or "ToLowerInvariant" => $"TO_LOWER({fieldName})",
				"ToUpper" or "ToUpperInvariant" => $"TO_UPPER({fieldName})",
				_ => throw new NotSupportedException($"String method {methodName} is not supported in ORDER BY.")
			};
		}

		throw new NotSupportedException($"Method {declaringType?.Name}.{methodName} is not supported in ORDER BY.");
	}
}
