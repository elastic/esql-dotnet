// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Reflection;
using Elastic.Esql.FieldMetadataResolver;
using Elastic.Mapping;

namespace Elastic.Clients.Esql;

/// <summary>
/// An <see cref="IEsqlFieldMetadataResolver"/> implementation that delegates to <see cref="TypeFieldMetadataResolver"/>.
/// </summary>
public sealed class MappingFieldMetadataResolver(IElasticsearchMappingContext? context = null) : IEsqlFieldMetadataResolver
{
	/// <summary>
	/// The underlying <see cref="TypeFieldMetadataResolver"/> used for field resolution and result materialization.
	/// </summary>
	public TypeFieldMetadataResolver MappingResolver { get; } = new(context);

	/// <inheritdoc/>
	public string GetFieldName(Type type, MemberInfo member) =>
		MappingResolver.Resolve(member);
}
