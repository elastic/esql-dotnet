// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Translation;

/// <summary>
/// Emits RENAME, EVAL, and KEEP commands from a translated projection result.
/// Shared by the Select pipeline and the join result-selector pipeline.
/// </summary>
internal sealed class ProjectionCommandEmitter(EsqlTranslationContext context)
{
	private readonly EsqlTranslationContext _context = context ?? throw new ArgumentNullException(nameof(context));

	/// <summary>
	/// Emits RENAME, EVAL, and KEEP commands in the correct order from a projection result.
	/// KEEP is always emitted to reduce the result set to only the projected fields.
	/// Active metadata fields requested on the source <c>FROM</c> are auto-retained unless
	/// the projection itself consumes them (e.g. via <c>EsqlMetadata.X</c> as a rename source).
	/// When <paramref name="renameCollisionFields"/> is provided (join projections), renames whose
	/// target collides with a field that still exists post-join are converted to EVALs, because
	/// ES|QL's RENAME fails if the target column already exists while EVAL overwrites it.
	/// </summary>
	public void Emit(SelectProjectionVisitor.ProjectionResult result, HashSet<string>? renameCollisionFields = null)
	{
		var safeRenames = new List<(string Source, string Target)>();
		var evalExpressions = new List<(string Field, string Expression)>(result.EvalExpressions);

		foreach (var (source, target) in result.RenameFields)
		{
			if (renameCollisionFields is not null && renameCollisionFields.Contains(target))
				evalExpressions.Add((target, source));
			else
				safeRenames.Add((source, target));
		}

		if (safeRenames.Count > 0)
			_context.Commands.Add(new RenameCommand(safeRenames));

		if (evalExpressions.Count > 0)
			_context.Commands.Add(new EvalCommand(evalExpressions.Select(e => $"{e.Field} = {e.Expression}").ToList()));

		var allKeepFields = new List<string>(result.KeepFields);
		foreach (var (_, target) in safeRenames)
			allKeepFields.Add(target);
		foreach (var (field, _) in evalExpressions)
			allKeepFields.Add(field);

		AppendRetainedMetadataNames(allKeepFields, result);

		if (allKeepFields.Count > 0)
			_context.Commands.Add(new KeepCommand(allKeepFields));
	}

	/// <summary>
	/// Appends active-metadata identifiers to <paramref name="keepFields"/> so they survive
	/// the auto-emitted KEEP. Metadata fields whose underscore-prefixed name was used as a
	/// rename source in the projection are skipped (they've been consumed by the projection).
	/// </summary>
	private void AppendRetainedMetadataNames(List<string> keepFields, SelectProjectionVisitor.ProjectionResult result)
	{
		if (_context.ActiveMetadata == MetadataField.None && !_context.ForkActive)
			return;

		var consumed = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (source, _) in result.RenameFields)
			_ = consumed.Add(source);

		foreach (var name in MetadataFieldHelper.EnumerateNames(_context.ActiveMetadata))
		{
			if (consumed.Contains(name))
				continue;

			if (keepFields.Contains(name))
				continue;

			keepFields.Add(name);
		}

		if (_context.ForkActive && !consumed.Contains("_fork") && !keepFields.Contains("_fork"))
			keepFields.Add("_fork");
	}
}
