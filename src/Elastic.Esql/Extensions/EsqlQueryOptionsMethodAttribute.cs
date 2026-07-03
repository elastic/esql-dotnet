// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Extensions;

/// <summary>
/// Marks a queryable extension method whose second argument carries query options.
/// The translator stores that argument in <see cref="EsqlQuery.QueryOptions"/> or
/// <see cref="EsqlQuery.ExecutorOptions"/> (by its runtime type) instead of translating the call
/// into an ES|QL command. Each options slot may be set only once per query chain.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EsqlQueryOptionsMethodAttribute : Attribute;
