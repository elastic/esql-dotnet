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
public class EsqlQuery(Type elementType, IReadOnlyList<QueryCommand> commands, EsqlParameters? parameters)
{
	/// <summary>
	/// The element type of the query results.
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
	/// Gets the FROM command if present.
	/// </summary>
	public FromCommand? From => Commands.OfType<FromCommand>().FirstOrDefault();

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
	/// Gets the ROW command if present.
	/// </summary>
	public RowCommand? Row => Commands.OfType<RowCommand>().FirstOrDefault();

	/// <summary>
	/// Gets all COMPLETION commands.
	/// </summary>
	public IEnumerable<CompletionCommand> CompletionCommands => Commands.OfType<CompletionCommand>();
}
