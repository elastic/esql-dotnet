// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using aspire;

var builder = DistributedApplication.CreateBuilder(args);

// Elasticsearch connection parameters (stored in dotnet user-secrets)
// Set via: dotnet user-secrets --project aspire set Parameters:ElasticsearchUrl "https://..."
// Set via: dotnet user-secrets --project aspire set Parameters:ElasticsearchApiKey "..."
var elasticsearchUrl = builder.AddParameter("ElasticsearchUrl", secret: false);
var elasticsearchApiKey = builder.AddParameter("ElasticsearchApiKey", secret: true);

// Ingest example project - populates Elasticsearch with fake data
builder.AddProject<Projects.Elastic_Examples_Ingest>(ResourceNames.ExamplesIngest)
	.WithEnvironment("ELASTICSEARCH_URL", elasticsearchUrl)
	.WithEnvironment("ELASTICSEARCH_APIKEY", elasticsearchApiKey);

// ES|QL example project - demonstrates ES|QL queries
builder.AddProject<Projects.Elastic_Examples_Esql>(ResourceNames.ExamplesEsql)
	.WithEnvironment("ELASTICSEARCH_URL", elasticsearchUrl)
	.WithEnvironment("ELASTICSEARCH_APIKEY", elasticsearchApiKey);

builder.Build().Run();
