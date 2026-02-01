// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

global using TUnit.Core;
global using AwesomeAssertions;
global using Elastic.Clients.Elasticsearch;
global using Elastic.Esql;
global using Elastic.Esql.Extensions;
global using Elastic.Esql.Core;
global using Elastic.Examples.Domain.Models;
global using Elastic.Examples.Ingest.Generators;
global using Elastic.Integration.Tests.Infrastructure;
global using LogLevel = Elastic.Examples.Domain.Models.LogLevel;

// Helper for casting IQueryable to IEsqlQueryable
namespace Elastic.Integration.Tests;

public static class QueryableExtensions
{
	public static Elastic.Esql.Core.IEsqlQueryable<T> AsEsql<T>(this IQueryable<T> query)
		=> (Elastic.Esql.Core.IEsqlQueryable<T>)query;
}
