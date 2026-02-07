// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Examples.Domain;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

namespace Elastic.Examples.Domain.Models;

/// <summary>
/// Application metrics for monitoring and alerting.
/// Demonstrates data stream pattern for metrics time-series data.
/// </summary>
public class ApplicationMetric : IConfigureElasticsearch<ApplicationMetricMappingsBuilder>
{
	[JsonPropertyName("@timestamp")]
	[Date(Format = "strict_date_optional_time")]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("metricset.name")]
	[Keyword]
	public string MetricSetName { get; set; } = string.Empty;

	[JsonPropertyName("service.name")]
	[Keyword]
	public string ServiceName { get; set; } = string.Empty;

	[JsonPropertyName("service.version")]
	[Keyword]
	public string? ServiceVersion { get; set; }

	[JsonPropertyName("host.name")]
	[Keyword]
	public string? HostName { get; set; }

	[JsonPropertyName("host.ip")]
	[Ip]
	public string? HostIp { get; set; }

	// System metrics
	[JsonPropertyName("system.cpu.total.pct")]
	public double? CpuPercent { get; set; }

	[JsonPropertyName("system.memory.used.pct")]
	public double? MemoryPercent { get; set; }

	[JsonPropertyName("system.memory.used.bytes")]
	public long? MemoryUsedBytes { get; set; }

	[JsonPropertyName("system.memory.total.bytes")]
	public long? MemoryTotalBytes { get; set; }

	// Application metrics
	[JsonPropertyName("app.requests.total")]
	public long? RequestsTotal { get; set; }

	[JsonPropertyName("app.requests.rate")]
	public double? RequestsPerSecond { get; set; }

	[JsonPropertyName("app.errors.total")]
	public long? ErrorsTotal { get; set; }

	[JsonPropertyName("app.latency.avg")]
	public double? LatencyAvgMs { get; set; }

	[JsonPropertyName("app.latency.p50")]
	public double? LatencyP50Ms { get; set; }

	[JsonPropertyName("app.latency.p95")]
	public double? LatencyP95Ms { get; set; }

	[JsonPropertyName("app.latency.p99")]
	public double? LatencyP99Ms { get; set; }

	// Business metrics
	[JsonPropertyName("business.orders.count")]
	public long? OrdersCount { get; set; }

	[JsonPropertyName("business.orders.value")]
	public double? OrdersValue { get; set; }

	[JsonPropertyName("business.cart.abandonments")]
	public long? CartAbandonments { get; set; }

	[JsonPropertyName("business.active_users")]
	public long? ActiveUsers { get; set; }

	// Database metrics
	[JsonPropertyName("db.connections.active")]
	public int? DbConnectionsActive { get; set; }

	[JsonPropertyName("db.connections.pool_size")]
	public int? DbConnectionPoolSize { get; set; }

	[JsonPropertyName("db.query.time.avg")]
	public double? DbQueryTimeAvgMs { get; set; }

	[Object]
	public MetricLabels? Labels { get; set; }

	/// <summary>Configures ApplicationMetric-specific analysis settings.</summary>
	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
		.Normalizer("metric_normalizer", n => n
			.Custom()
			.Filter(TokenFilters.Lowercase));

	/// <summary>Configures ApplicationMetric-specific mapping overrides.</summary>
	public static ApplicationMetricMappingsBuilder ConfigureMappings(ApplicationMetricMappingsBuilder mappings) => mappings
		.ServiceName(f => f
			.Normalizer(mappings.Analysis.Normalizers.MetricNormalizer))
		.AddRuntimeField("memory_pct_computed", r => r
			.Double()
			.Script("""
				if (doc['system.memory.used.bytes'].size() > 0 && doc['system.memory.total.bytes'].size() > 0) {
					double used = doc['system.memory.used.bytes'].value;
					double total = doc['system.memory.total.bytes'].value;
					if (total > 0) emit(used / total * 100);
				}
				"""))
		.AddRuntimeField("error_rate_pct", r => r
			.Double()
			.Script("""
				if (doc['app.errors.total'].size() > 0 && doc['app.requests.total'].size() > 0) {
					emit(doc['app.errors.total'].value / (double)doc['app.requests.total'].value * 100);
				}
				"""))
		.AddRuntimeField("latency_category", r => r
			.Keyword()
			.Script("""
				if (doc['app.latency.p95'].size() > 0) {
					double p95 = doc['app.latency.p95'].value;
					if (p95 < 100) emit('fast');
					else if (p95 < 500) emit('normal');
					else if (p95 < 1000) emit('slow');
					else emit('critical');
				}
				"""))
		.AddRuntimeField("is_healthy", r => r
			.Boolean()
			.Script("""
				boolean healthy = true;
				if (doc['system.cpu.total.pct'].size() > 0 && doc['system.cpu.total.pct'].value > 80) healthy = false;
				if (doc['system.memory.used.pct'].size() > 0 && doc['system.memory.used.pct'].value > 90) healthy = false;
				emit(healthy);
				"""));
}

/// <summary>
/// Custom labels/dimensions for the metric.
/// </summary>
public class MetricLabels
{
	[Keyword]
	public string? Endpoint { get; set; }

	[Keyword]
	public string? Method { get; set; }

	[Keyword]
	public string? Database { get; set; }

	[Keyword]
	public string? Region { get; set; }
}
