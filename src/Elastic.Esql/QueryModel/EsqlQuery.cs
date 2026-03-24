// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.QueryModel;

/// <summary>
/// Represents an intermediate ES|QL query model.
/// </summary>
/// <param name="elementType">The element type.</param>
/// <param name="commands">The ESQL query commands.</param>
/// <param name="parameters">The ESQL query parameters.</param>
/// <param name="queryOptions">Opaque query options extracted from the expression tree.</param>
public sealed class EsqlQuery(Type elementType, IReadOnlyList<QueryCommand> commands, EsqlParameters? parameters, object? queryOptions = null)
{
	/// <summary>
	/// The element type of the query.
	/// </summary>
	public Type ElementType { get; } = elementType ?? throw new ArgumentNullException(nameof(elementType));

	/// <summary>
	/// The commands in this query.
	/// </summary>
	public IReadOnlyList<QueryCommand> Commands { get; } = commands ?? throw new ArgumentNullException(nameof(commands));

	/// <summary>
	/// Gets the collection of ESQL query parameters.
	/// </summary>
	public EsqlParameters? Parameters { get; } = parameters;

	/// <summary>
	/// Gets the opaque query options extracted from the expression tree.
	/// The concrete type is defined by the downstream executor implementation.
	/// </summary>
	public object? QueryOptions { get; } = queryOptions;

	/// <summary>
	/// Get the source command (e.g. FROM, ROW, etc.) if present.
	/// </summary>
	public SourceCommand? Source => Commands.OfType<SourceCommand>().SingleOrDefault();

	/// <summary>
	/// Gets the FROM command if present.
	/// </summary>
	public FromCommand? From => Commands.OfType<FromCommand>().SingleOrDefault();

	/// <summary>
	/// Gets the ROW command if present.
	/// </summary>
	public RowCommand? Row => Commands.OfType<RowCommand>().SingleOrDefault();

	/// <summary>
	/// Gets all WHERE commands.
	/// </summary>
	public IEnumerable<WhereCommand> WhereCommands => Commands.OfType<WhereCommand>();

	/// <summary>
	/// Gets the LIMIT command if present.
	/// </summary>
	public LimitCommand? Limit => Commands.OfType<LimitCommand>().LastOrDefault();

	/// <summary>
	/// Gets all SORT commands.
	/// </summary>
	public IEnumerable<SortCommand> SortCommands => Commands.OfType<SortCommand>();

	/// <summary>
	/// Gets all COMPLETION commands.
	/// </summary>
	public IEnumerable<CompletionCommand> CompletionCommands => Commands.OfType<CompletionCommand>();

	/// <summary>
	/// Gets all LOOKUP JOIN commands.
	/// </summary>
	public IEnumerable<LookupJoinCommand> LookupJoinCommands => Commands.OfType<LookupJoinCommand>();

	/// <summary>Creates a copy with a different command list.</summary>
	public EsqlQuery WithCommands(IReadOnlyList<QueryCommand> commands) =>
		new(ElementType, commands, Parameters, QueryOptions);

	/// <summary>Creates a copy with different parameters.</summary>
	public EsqlQuery WithParameters(EsqlParameters? parameters) =>
		new(ElementType, Commands, parameters, QueryOptions);

	/// <summary>Creates a copy with the source command set to FROM with the specified index pattern. Replaces an existing source command or prepends one.</summary>
	public EsqlQuery WithSource(string indexPattern)
	{
		var list = Commands.ToList();
		var existing = list.FindIndex(c => c is SourceCommand);
		if (existing >= 0)
			list[existing] = new FromCommand(indexPattern);
		else
			list.Insert(0, new FromCommand(indexPattern));
		return new(ElementType, list, Parameters, QueryOptions);
	}

	/// <summary>Creates a copy with the LIMIT set to the specified count. Replaces an existing LIMIT or appends one.</summary>
	public EsqlQuery WithLimit(int count)
	{
		var list = Commands.ToList();
		var existing = list.FindLastIndex(c => c is LimitCommand);
		if (existing >= 0)
			list[existing] = new LimitCommand(count);
		else
			list.Add(new LimitCommand(count));
		return new(ElementType, list, Parameters, QueryOptions);
	}
}
