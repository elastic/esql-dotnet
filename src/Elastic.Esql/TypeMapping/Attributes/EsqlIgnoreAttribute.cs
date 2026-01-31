// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.TypeMapping.Attributes;

/// <summary>
/// Indicates that a property should be ignored in ES|QL queries.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class EsqlIgnoreAttribute : Attribute
{
}
