// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the LIMIT command.
/// </summary>
public class LimitCommand : QueryCommand
{
	public int Count { get; }

	public LimitCommand(int count)
	{
		if (count < 0)
			throw new ArgumentOutOfRangeException(nameof(count), "Limit count must be non-negative.");
		Count = count;
	}

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
