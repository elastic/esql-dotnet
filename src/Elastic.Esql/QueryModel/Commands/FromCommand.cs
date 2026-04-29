// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the FROM command.
/// </summary>
public sealed class FromCommand(string indexPattern, MetadataField metadata = MetadataField.None) : SourceCommand
{
	public string IndexPattern { get; } = indexPattern ?? throw new ArgumentNullException(nameof(indexPattern));

	/// <summary>The metadata fields to request via the <c>METADATA</c> directive.</summary>
	public MetadataField Metadata { get; } = metadata;

	internal override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
