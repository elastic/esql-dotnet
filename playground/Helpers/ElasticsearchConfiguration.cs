// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Microsoft.Extensions.Configuration;
using Playground.Models;

namespace Playground.Helpers;

public static class ElasticsearchConfiguration
{
	public static (string url, string apiKey) Get()
	{
		var urlEnv = Environment.GetEnvironmentVariable("ELASTICSEARCH_URL");
		var apiKeyEnv = Environment.GetEnvironmentVariable("ELASTICSEARCH_APIKEY");

		if (!string.IsNullOrEmpty(urlEnv) && !string.IsNullOrEmpty(apiKeyEnv))
			return (urlEnv, apiKeyEnv);

		var config = new ConfigurationBuilder()
			.AddUserSecrets(typeof(SupportTicket).Assembly, optional: true)
			.Build();

		var urlConfig = config["Parameters:ElasticsearchUrl"];
		var apiKeyConfig = config["Parameters:ElasticsearchApiKey"];

		if (string.IsNullOrEmpty(urlConfig) || string.IsNullOrEmpty(apiKeyConfig))
		{
			Console.WriteLine("Set ELASTICSEARCH_URL and ELASTICSEARCH_APIKEY environment variables");
			Environment.Exit(1);
		}

		return (urlConfig, apiKeyConfig);
	}
}
