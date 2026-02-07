// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.Extensions;

namespace Playground.Helpers;

public static class QueryRunner
{
	public static async Task RunAsync<T>(string description, IQueryable<T> query)
	{
		Console.WriteLine($">> {description}");
		var esql = query.ToEsqlString();
		Console.WriteLine($"   ES|QL: {esql.Replace("\n", " ")}");

		try
		{
			if (query is IEsqlQueryable<T> eq)
			{
				var results = await eq.ToListAsync();
				Console.WriteLine($"   Results: {results.Count} row(s)");

				// Show condensed output for demo
				foreach (var r in results.Take(3))
				{
					var json = JsonSerializer.Serialize(r);
					// Truncate long JSON for readability
					if (json.Length > 120)
						json = json[..120] + "...";
					Console.WriteLine($"   - {json}");
				}
				if (results.Count > 3)
					Console.WriteLine($"   ... and {results.Count - 3} more");
			}
		}
		catch (EsqlExecutionException ex)
		{
			Console.WriteLine($"   Error: {ex.Message}");
			if (ex.ResponseBody != null)
			{
				try
				{
					using var doc = JsonDocument.Parse(ex.ResponseBody);
					if (doc.RootElement.TryGetProperty("error", out var error) &&
						error.TryGetProperty("reason", out var reason))
						Console.WriteLine($"   Reason: {reason.GetString()}");
				}
				catch { /* ignore parse errors */ }
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"   Error: {ex.Message}");
		}
		Console.WriteLine();
	}
}
