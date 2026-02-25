// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.QueryModel;

/// <summary>
/// Base class for all ES|QL query commands.
/// </summary>
public abstract class QueryCommand
{
	/// <summary>
	/// Accepts a visitor for generating ES|QL.
	/// </summary>
	public abstract void Accept(ICommandVisitor visitor);
}

/// <summary>
/// Visitor interface for processing query commands.
/// </summary>
public interface ICommandVisitor
{
	void Visit(FromCommand command);
	void Visit(WhereCommand command);
	void Visit(EvalCommand command);
	void Visit(StatsCommand command);
	void Visit(SortCommand command);
	void Visit(LimitCommand command);
	void Visit(KeepCommand command);
	void Visit(DropCommand command);
	void Visit(CompletionCommand command);
	void Visit(RowCommand command);
	void Visit(LookupJoinCommand command);
}
