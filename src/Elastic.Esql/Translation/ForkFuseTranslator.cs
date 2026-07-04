// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics;
using System.Linq.Expressions;

using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Translates the <c>Fork</c> and <c>Fuse</c> extension-method calls into
/// <see cref="ForkCommand"/> and <see cref="FuseCommand"/>, owning fork branch translation
/// and the FUSE-after-FORK validation rules.
/// </summary>
internal sealed class ForkFuseTranslator(EsqlQueryProvider provider, EsqlTranslationContext context, bool inlineParameters)
{
	private readonly EsqlQueryProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));

	// Captured when FORK is translated; FUSE validates its weights against this count.
	private int _lastForkBranchCount;

	public void TranslateFork(MethodCallExpression node)
	{
		if (_context.InsideForkBranch)
			throw new InvalidOperationException(
				"Nested 'Fork' is not supported: a 'Fork' command cannot appear inside another fork's branch lambda.");

		if (_context.Commands.OfType<ForkCommand>().Any())
			throw new InvalidOperationException(
				"Only one 'Fork' command is supported per query (per the ES|QL spec).");

		if (node.Arguments.Count < 2)
			throw new NotSupportedException("Fork requires at least one branch.");

		var branchesArg = ExpressionConstantResolver.Resolve(node.Arguments[1]);
		if (branchesArg is not Array branchesArray || branchesArray.Length == 0)
			throw new NotSupportedException("Fork requires at least one branch.");

		var elementType = _context.ElementType
			?? throw new InvalidOperationException("Fork must follow a typed source command (FROM or ROW).");

		var branches = new List<ForkBranch>(branchesArray.Length);
		var inheritedMetadata = _context.ActiveMetadata;

		for (var i = 0; i < branchesArray.Length; i++)
		{
			if (branchesArray.GetValue(i) is not LambdaExpression branchLambda)
				throw new NotSupportedException($"Fork branch {i + 1} must be a lambda expression.");

			var branch = ForkBranchVisitor.Translate(_provider, branchLambda, elementType, inheritedMetadata, inlineParameters, _context);
			if (branch.Fragments.Count == 0)
				throw new NotSupportedException($"Fork branch {i + 1} produced no commands.");

			branches.Add(branch);
		}

		_context.Commands.Add(new ForkCommand(branches));
		_context.ForkActive = true;
		_lastForkBranchCount = branchesArray.Length;
	}

	public void TranslateFuse(MethodCallExpression node)
	{
		// Fuse parameters: (source, method, rankConstant, normalizer, weights, score, group, key)
		Debug.Assert(node.Arguments.Count == 8, "Fuse extension method always passes 8 arguments.");

		var method = (FuseMethod)(ExpressionConstantResolver.Resolve(node.Arguments[1]) ?? FuseMethod.Rrf);
		var rankConstant = ExpressionConstantResolver.Resolve(node.Arguments[2]) as int?;
		var normalizer = (ScoreNormalizer)(ExpressionConstantResolver.Resolve(node.Arguments[3]) ?? ScoreNormalizer.None);
		var weights = ExpressionConstantResolver.Resolve(node.Arguments[4]) as double[];
		var scoreLambda = ExpressionConstantResolver.Resolve(node.Arguments[5]) as LambdaExpression;
		var groupLambda = ExpressionConstantResolver.Resolve(node.Arguments[6]) as LambdaExpression;
		var keyLambda = ExpressionConstantResolver.Resolve(node.Arguments[7]) as LambdaExpression;

		ValidateFuseFollowsFork(weights);

		var scoreColumn = scoreLambda is not null ? ResolveSingleColumnFromLambda(scoreLambda, nameof(EsqlQueryableExtensions.Fuse), "score") : null;
		var groupColumn = groupLambda is not null ? ResolveSingleColumnFromLambda(groupLambda, nameof(EsqlQueryableExtensions.Fuse), "group") : null;
		var keyColumns = keyLambda is not null ? ResolveKeyColumnsFromLambda(keyLambda) : null;

		_context.Commands.Add(new FuseCommand(
			method: method,
			rankConstant: rankConstant,
			normalizer: normalizer,
			weights: weights,
			scoreColumn: scoreColumn,
			groupColumn: groupColumn,
			keyColumns: keyColumns));

		// Once Fuse merges the fork branches, the _fork discriminator is consumed; downstream
		// projections should not auto-retain it.
		_context.ForkActive = false;
	}

	private void ValidateFuseFollowsFork(double[]? weights)
	{
		// Fuse requires a Fork earlier in the pipeline (not necessarily immediately preceding).
		// ES|QL allows row-shape transformations like DROP / KEEP / RENAME / EVAL / WHERE between
		// them -- in particular DROP is recommended to remove dense_vector columns that FUSE rejects.
		// Aggregations (STATS) however collapse the fork-discriminator column and break FUSE.
		ForkCommand? matchingFork = null;
		for (var i = _context.Commands.Count - 1; i >= 0; i--)
		{
			var cmd = _context.Commands[i];
			if (cmd is ForkCommand fork)
			{
				matchingFork = fork;
				break;
			}

			if (cmd is StatsCommand)
				throw new InvalidOperationException("'Fuse' cannot follow a 'Stats' / aggregation command; aggregations break the FORK row layout.");
		}

		if (matchingFork is null)
			throw new InvalidOperationException("'Fuse' must follow a 'Fork' command earlier in the pipeline.");

		if (weights is not null && weights.Length != _lastForkBranchCount)
			throw new ArgumentException(
				$"Fuse weights count ({weights.Length}) must match the preceding Fork branch count ({_lastForkBranchCount}).",
				nameof(weights));

		// Per the ES|QL FUSE docs (Stack 9.4+ / Serverless), each FORK branch must contain a LIMIT
		// before FUSE. Older versions inject an implicit LIMIT 1000, but we validate eagerly so the
		// translator gives a clear error rather than relying on the server response.
		for (var b = 0; b < matchingFork.Branches.Count; b++)
		{
			if (!matchingFork.Branches[b].HasLimit)
				throw new InvalidOperationException(
					$"Fork branch {b + 1} must include a 'Take(...)' (LIMIT) before 'Fuse'. " +
					"ES|QL requires a LIMIT inside each FORK branch when followed by FUSE.");
		}
	}

	private string ResolveSingleColumnFromLambda(LambdaExpression lambda, string commandName, string parameterName)
	{
		var body = lambda.Body.UnwrapConvertExpressions();

		// EsqlMetadata.X marker access -> emit underscore-prefixed identifier.
		if (body is MemberExpression { Expression: null, Member: { } metaMember }
			&& metaMember.DeclaringType == typeof(EsqlMetadata))
			return _context.ResolveMetadataMemberOrThrow(metaMember.Name);

		// Parameter-rooted member access (e.g. x => x.Score).
		if (body is MemberExpression member && ExpressionTranslationHelpers.IsRootedInParameter(member))
			return body.ResolveFieldName(_context.Metadata);

		throw new NotSupportedException(
			$"'{commandName}({parameterName}:)' must reference a single column " +
			$"(a parameter-rooted property or '{nameof(EsqlMetadata)}.X' marker), got '{body.NodeType}'.");
	}

	private List<string> ResolveKeyColumnsFromLambda(LambdaExpression lambda)
	{
		var body = lambda.Body.UnwrapConvertExpressions();

		// Composite key via anonymous type: x => new { x.Id, x.Index } or new { Id = EsqlMetadata.Id, Index = EsqlMetadata.Index }
		if (body is NewExpression newExpression)
		{
			if (newExpression.Members is null)
				throw new NotSupportedException(
					"Composite 'Fuse(key:)' must use an anonymous type, e.g. 'x => new { x.Id, x.Index }'.");

			var result = new List<string>(newExpression.Arguments.Count);
			for (var i = 0; i < newExpression.Arguments.Count; i++)
			{
				var arg = newExpression.Arguments[i].UnwrapConvertExpressions();

				if (arg is MemberExpression { Expression: null, Member: { } metaMember }
					&& metaMember.DeclaringType == typeof(EsqlMetadata))
				{
					result.Add(_context.ResolveMetadataMemberOrThrow(metaMember.Name));
					continue;
				}

				if (arg is MemberExpression keyMember && ExpressionTranslationHelpers.IsRootedInParameter(keyMember))
				{
					result.Add(arg.ResolveFieldName(_context.Metadata));
					continue;
				}

				throw new NotSupportedException(
					$"Each 'Fuse(key:)' member must be a parameter-rooted property or '{nameof(EsqlMetadata)}.X' marker, got '{arg.NodeType}'.");
			}
			return result;
		}

		// Single key column.
		return [ResolveSingleColumnFromLambda(lambda, nameof(EsqlQueryableExtensions.Fuse), "key")];
	}
}
