// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Integration.Tests.Models;

public class TestEvent
{
	[JsonPropertyName("@timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("log.level")]
	public string Level { get; set; } = string.Empty;

	[JsonPropertyName("service.name")]
	public string ServiceName { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("http.status_code")]
	public int? HttpStatusCode { get; set; }

	[JsonPropertyName("event.duration")]
	public long? DurationNanos { get; set; }

	[JsonPropertyName("host.ip")]
	public string? HostIp { get; set; }
}
