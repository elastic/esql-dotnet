// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Core;

/// <summary>
/// Intercepts a translated ES|QL query before formatting and execution,
/// allowing structured inspection and modification of the query model.
/// </summary>
public interface IEsqlQueryInterceptor
{
	/// <summary>
	/// Intercepts the translated query. Return the original query unchanged, or a modified copy.
	/// </summary>
	EsqlQuery Intercept(EsqlQuery query);
}
