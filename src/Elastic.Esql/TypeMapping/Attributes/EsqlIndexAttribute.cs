// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.TypeMapping.Attributes;

/// <summary>
/// Specifies the Elasticsearch index pattern for a type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class EsqlIndexAttribute(string indexPattern) : Attribute
{
	public string IndexPattern { get; } = indexPattern ?? throw new ArgumentNullException(nameof(indexPattern));
}
