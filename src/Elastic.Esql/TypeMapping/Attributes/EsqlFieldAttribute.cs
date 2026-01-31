// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.TypeMapping.Attributes;

/// <summary>
/// Specifies the ES|QL field name for a property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class EsqlFieldAttribute(string fieldName) : Attribute
{
	public string FieldName { get; } = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
}
