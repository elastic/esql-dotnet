// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elastic.Examples.Ingest;

/// <summary>
/// Helper for detecting and adapting to Elasticsearch Serverless environments.
/// </summary>
public static class ServerlessHelper
{
	private static readonly string[] ServerlessHostPatterns =
	[
		".elastic.cloud",
		".elastic-cloud.com",
		".found.io"
	];

	private static readonly string[] UnsupportedSettings =
	[
		"number_of_shards",
		"number_of_replicas",
		"refresh_interval"  // Serverless requires >= 5s, so we remove it to use default
	];

	/// <summary>
	/// Detects if the given URL points to an Elasticsearch Serverless instance.
	/// </summary>
	public static bool IsServerless(string url)
	{
		if (string.IsNullOrEmpty(url))
			return false;

		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
			return false;

		return ServerlessHostPatterns.Any(pattern =>
			uri.Host.EndsWith(pattern, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Returns a settings modifier that removes serverless-incompatible settings.
	/// Returns null if not running on serverless (no modification needed).
	/// </summary>
	public static Func<string, string>? GetSettingsModifier(string url) =>
		IsServerless(url) ? StripUnsupportedSettings : null;

	/// <summary>
	/// Removes settings that are not supported in serverless mode.
	/// </summary>
	public static string StripUnsupportedSettings(string settingsJson)
	{
		try
		{
			var node = JsonNode.Parse(settingsJson);
			if (node is not JsonObject root)
				return settingsJson;

			// Settings can be at root level or under "settings" key
			var settingsNode = root["settings"] as JsonObject ?? root;

			foreach (var key in UnsupportedSettings)
			{
				_ = settingsNode.Remove(key);
				// Also try with "index." prefix
				_ = settingsNode.Remove($"index.{key}");
			}

			// If the settings object is now empty, return minimal JSON
			if (settingsNode.Count == 0)
			{
				if (root.ContainsKey("settings"))
					return """{"settings":{}}""";
				return "{}";
			}

			return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
		}
		catch
		{
			// If parsing fails, return original
			return settingsJson;
		}
	}
}
