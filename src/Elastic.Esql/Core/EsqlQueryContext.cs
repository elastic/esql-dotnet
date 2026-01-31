// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.TypeMapping;

namespace Elastic.Esql.Core;

/// <summary>
/// Context for ES|QL query execution.
/// </summary>
/// <remarks>
/// Creates a new query context.
/// </remarks>
public class EsqlQueryContext(EsqlClientSettings settings)
{
	/// <summary>
	/// The client settings.
	/// </summary>
	public EsqlClientSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

	/// <summary>
	/// The field name resolver.
	/// </summary>
	public FieldNameResolver FieldNameResolver { get; } = new FieldNameResolver();

	/// <summary>
	/// Explicit index pattern override. When set, this takes precedence over the type's EsqlIndex attribute.
	/// </summary>
	public string? IndexPattern { get; set; }

	/// <summary>
	/// The cancellation token for query execution.
	/// </summary>
	public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
