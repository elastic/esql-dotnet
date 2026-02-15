// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.QueryModel;

/// <summary>
/// Represents an intermediate ES|QL query model.
/// </summary>
public class EsqlQuery
{
	private readonly List<QueryCommand> _commands = [];

	/// <summary>
	/// The element type of the query results.
	/// </summary>
	public Type? ElementType { get; set; }

	/// <summary>
	/// The commands in this query.
	/// </summary>
	public IReadOnlyList<QueryCommand> Commands => _commands;

	/// <summary>
	/// Adds a command to the query.
	/// </summary>
	public void AddCommand(QueryCommand command) => _commands.Add(command);

	/// <summary>
	/// Creates a copy of this query.
	/// </summary>
	public EsqlQuery Clone()
	{
		var clone = new EsqlQuery { ElementType = ElementType };
		foreach (var command in _commands)
			clone._commands.Add(command);
		return clone;
	}

	/// <summary>
	/// Gets the FROM command if present.
	/// </summary>
	public FromCommand? From => _commands.OfType<FromCommand>().FirstOrDefault();

	/// <summary>
	/// Gets all WHERE commands.
	/// </summary>
	public IEnumerable<WhereCommand> WhereCommands => _commands.OfType<WhereCommand>();

	/// <summary>
	/// Gets the LIMIT command if present.
	/// </summary>
	public LimitCommand? Limit => _commands.OfType<LimitCommand>().LastOrDefault();

	/// <summary>
	/// Gets all SORT commands.
	/// </summary>
	public IEnumerable<SortCommand> SortCommands => _commands.OfType<SortCommand>();

	/// <summary>
	/// Gets the ROW command if present.
	/// </summary>
	public RowCommand? Row => _commands.OfType<RowCommand>().FirstOrDefault();

	/// <summary>
	/// Gets all COMPLETION commands.
	/// </summary>
	public IEnumerable<CompletionCommand> CompletionCommands => _commands.OfType<CompletionCommand>();
}
