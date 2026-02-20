// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates LINQ OrderBy expressions to ES|QL SORT commands.
/// </summary>
internal sealed class OrderByVisitor(EsqlTranslationContext context) : ExpressionVisitor
{
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));

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
			MethodCallExpression methodCall when methodCall.Method.DeclaringType != typeof(GeneralPurposeExtensions) =>
				TranslateMethodCall(methodCall),
			_ => expression.ResolveFieldName(_context.FieldMetadataResolver)
		};

	private string TranslateMethodCall(MethodCallExpression methodCall)
	{
		var methodName = methodCall.Method.Name;
		var declaringType = methodCall.Method.DeclaringType;

		// String methods that can be used for sorting
		if (declaringType == typeof(string) && methodCall.Object is not null)
		{
			var fieldName = methodCall.Object.ResolveFieldName(_context.FieldMetadataResolver);

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
