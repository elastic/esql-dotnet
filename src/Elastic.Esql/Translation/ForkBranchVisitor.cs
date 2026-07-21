// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Core;
using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates a single <c>Fork</c> branch lambda into a <see cref="ForkBranch"/>.
/// The branch's <see cref="LambdaExpression.Parameters"/>[0] is substituted with the
/// parent's source expression via <see cref="ExpressionVisitor"/>, then the rewritten expression
/// is translated through the same <see cref="EsqlExpressionVisitor"/> machinery used for
/// top-level queries. The resulting commands are formatted individually so they can be wrapped
/// in parentheses inside <c>FORK ( ... )</c>.
/// </summary>
internal static class ForkBranchVisitor
{
	public static ForkBranch Translate(
		EsqlQueryProvider provider,
		LambdaExpression branchLambda,
		Type elementType,
		MetadataField inheritedMetadata,
		bool inlineParameters,
		EsqlTranslationContext parentContext)
	{
		// Substitute the branch's input parameter with a synthetic root expression.
		var branchParameter = branchLambda.Parameters[0];
		var rootExpression = Expression.Constant(null, branchParameter.Type);
		var substitutor = new ParameterSubstitutor(branchParameter, rootExpression);
		var rewrittenBody = substitutor.Visit(branchLambda.Body)
			?? throw new NotSupportedException("Fork branch lambda body could not be rewritten.");

		var visitor = new EsqlExpressionVisitor(provider, inlineParameters);
		visitor.Context.ElementType = elementType;
		visitor.Context.ActiveMetadata = inheritedMetadata;
		visitor.Context.InsideForkBranch = true;

		// Share the parent's parameter accumulator so closure-captured values inside branches
		// land in the final params payload (and uniquely-suffixed names are reserved across branches).
		visitor.Context.Parameters = parentContext.Parameters;

		var query = visitor.Translate(rewrittenBody);

		// Format each command individually so the FORK formatter can join them with " | ".
		var fragments = new List<string>();
		var formatter = new EsqlFormatter();
		foreach (var command in query.Commands)
		{
			var single = new EsqlQuery(elementType, [command], parameters: null, queryOptions: null);
			fragments.Add(formatter.Format(single));
		}

		// FUSE requires a LIMIT in every FORK branch. Track it on the command model here instead
		// of string-sniffing formatted fragments during validation. Raw fragments are opaque
		// strings by nature, so those alone are still inspected textually.
		var hasLimit = query.Commands.Any(command => command switch
		{
			LimitCommand => true,
			RawFragmentCommand raw =>
				raw.Fragment.StartsWith("LIMIT ", StringComparison.Ordinal) || raw.Fragment.Equals("LIMIT", StringComparison.Ordinal),
			_ => false
		});

		return new ForkBranch(fragments, hasLimit);
	}

	/// <summary>
	/// Replaces every occurrence of <see cref="ParameterExpression"/> <c>target</c> in an
	/// expression tree with <c>replacement</c>. Used to splice the parent's source expression
	/// into a fork branch lambda body.
	/// </summary>
	private sealed class ParameterSubstitutor(ParameterExpression target, Expression replacement) : ExpressionVisitor
	{
		protected override Expression VisitParameter(ParameterExpression node) =>
			node == target ? replacement : base.VisitParameter(node);
	}
}
