// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using Elastic.Esql.Core;
using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates a single <c>Fork</c> branch lambda into an ordered list of ES|QL pipeline
/// fragments. The branch is evaluated against a synthetic <see cref="EsqlQueryable{TSource}"/>
/// rooted at the parent's <see cref="EsqlQueryProvider"/>, then translated through the same
/// <see cref="EsqlExpressionVisitor"/> machinery used for top-level queries. The resulting
/// commands are formatted individually so they can be wrapped in parentheses inside <c>FORK ( ... )</c>.
/// </summary>
internal static class ForkBranchVisitor
{
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Branch root queryable construction; element type is known at runtime.")]
	[UnconditionalSuppressMessage("AOT", "IL2055", Justification = "Branch root queryable construction; element type is known at runtime.")]
	public static IReadOnlyList<string> Translate(
		EsqlQueryProvider provider,
		LambdaExpression branchLambda,
		Type elementType,
		MetadataField inheritedMetadata,
		bool inlineParameters,
		EsqlTranslationContext parentContext)
	{
		// Compile and invoke the branch lambda against a synthetic root IQueryable<T>.
		var queryableType = typeof(EsqlQueryable<>).MakeGenericType(elementType);
		var rootQueryable = Activator.CreateInstance(queryableType, provider)!;

		var compiled = branchLambda.Compile();
		var branchResult = compiled.DynamicInvoke(rootQueryable)
			?? throw new NotSupportedException("Fork branch lambda returned null.");

		var branchQueryable = (IQueryable)branchResult;

		var visitor = new EsqlExpressionVisitor(provider, inlineParameters);
		visitor.Context.ElementType = elementType;
		visitor.Context.ActiveMetadata = inheritedMetadata;

		// Share the parent's parameter accumulator so closure-captured values inside branches
		// land in the final params payload (and uniquely-suffixed names are reserved across branches).
		visitor.Context.Parameters = parentContext.Parameters;

		var query = visitor.Translate(branchQueryable.Expression);

		// Format each command individually so the FORK formatter can join them with " | ".
		var fragments = new List<string>();
		var formatter = new EsqlFormatter();
		foreach (var command in query.Commands)
		{
			var single = new EsqlQuery(elementType, [command], parameters: null, queryOptions: null);
			fragments.Add(formatter.Format(single));
		}

		return fragments;
	}
}
