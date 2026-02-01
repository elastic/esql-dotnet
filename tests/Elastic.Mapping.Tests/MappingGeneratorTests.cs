// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

public class MappingGeneratorTests
{
	[Test]
	public void Index_GeneratesStaticMappingClass()
	{
		// The Mapping class should be generated
		var hash = LogEntry.Mapping.Hash;
		hash.Should().NotBeNullOrEmpty();
		hash.Should().HaveLength(16);
	}

	[Test]
	public void Index_GeneratesFieldConstants()
	{
		// Field constants should be generated from JsonPropertyName or camelCase
		LogEntry.Mapping.Fields.Timestamp.Should().Be("@timestamp");
		LogEntry.Mapping.Fields.Level.Should().Be("log.level");
		LogEntry.Mapping.Fields.Message.Should().Be("message");
		LogEntry.Mapping.Fields.StatusCode.Should().Be("statusCode");
	}

	[Test]
	public void Index_GeneratesIndexStrategy()
	{
		var strategy = LogEntry.Mapping.IndexStrategy;
		strategy.WriteTarget.Should().Be("logs-write");
	}

	[Test]
	public void Index_GeneratesSearchStrategy()
	{
		var strategy = LogEntry.Mapping.SearchStrategy;
		strategy.Pattern.Should().Be("logs-*");
		strategy.ReadAlias.Should().Be("logs-read");
	}

	[Test]
	public void Index_GeneratesSettingsJson()
	{
		var json = LogEntry.Mapping.GetSettingsJson();
		json.Should().Contain("\"number_of_shards\": 3");
		json.Should().Contain("\"number_of_replicas\": 2");
	}

	[Test]
	public void Index_GeneratesMappingJson()
	{
		var json = LogEntry.Mapping.GetMappingJson();
		json.Should().Contain("\"@timestamp\"");
		json.Should().Contain("\"type\": \"date\"");
		json.Should().Contain("\"type\": \"keyword\"");
		json.Should().Contain("\"type\": \"text\"");
		json.Should().Contain("\"type\": \"ip\"");
		// InternalId should not be in mapping (JsonIgnore)
		json.Should().NotContain("internalId");
	}

	[Test]
	public void Index_GeneratesIndexJson()
	{
		var json = LogEntry.Mapping.GetIndexJson();
		json.Should().Contain("\"settings\":");
		json.Should().Contain("\"mappings\":");
	}

	[Test]
	public void Index_HashIsStable()
	{
		// Hash should be deterministic
		var hash1 = LogEntry.Mapping.Hash;
		var hash2 = LogEntry.Mapping.Hash;
		hash1.Should().Be(hash2);
	}

	[Test]
	public void Index_SeparateHashesProvided()
	{
		// Separate hashes for settings and mappings
		var settingsHash = LogEntry.Mapping.SettingsHash;
		var mappingsHash = LogEntry.Mapping.MappingsHash;
		var combinedHash = LogEntry.Mapping.Hash;

		settingsHash.Should().NotBeNullOrEmpty();
		mappingsHash.Should().NotBeNullOrEmpty();
		settingsHash.Should().NotBe(mappingsHash);
	}

	[Test]
	public void DataStream_GeneratesCorrectStrategy()
	{
		var indexStrategy = NginxAccessLog.Mapping.IndexStrategy;
		indexStrategy.DataStreamName.Should().Be("logs-nginx.access-production");
		indexStrategy.Type.Should().Be("logs");
		indexStrategy.Dataset.Should().Be("nginx.access");
		indexStrategy.Namespace.Should().Be("production");

		var searchStrategy = NginxAccessLog.Mapping.SearchStrategy;
		searchStrategy.Pattern.Should().Be("logs-nginx.access-*");
	}

	[Test]
	public void SimpleDocument_InfersTypesFromClrTypes()
	{
		var json = SimpleDocument.Mapping.GetMappingJson();
		json.Should().Contain("\"name\": { \"type\": \"keyword\"");
		json.Should().Contain("\"value\": { \"type\": \"integer\"");
		json.Should().Contain("\"createdAt\": { \"type\": \"date\"");
	}

	[Test]
	public void AdvancedDocument_SupportsSpecializedTypes()
	{
		var json = AdvancedDocument.Mapping.GetMappingJson();
		json.Should().Contain("\"type\": \"geo_point\"");
		json.Should().Contain("\"type\": \"dense_vector\"");
		json.Should().Contain("\"dims\": 384");
		json.Should().Contain("\"similarity\": \"cosine\"");
		json.Should().Contain("\"type\": \"semantic_text\"");
		json.Should().Contain("\"type\": \"completion\"");
		json.Should().Contain("\"type\": \"nested\"");
	}

	[Test]
	public void MappingConfig_AllowsFluentOverrides()
	{
		var config = LogEntry.MappingConfig()
			.Message(f => f.Analyzer("english"));

		var json = config.GetMappingJson();
		json.Should().Contain("english");
	}

	[Test]
	public void MappingConfig_ComputesHash()
	{
		var config = LogEntry.MappingConfig();
		var hash = config.ComputeHash();
		hash.Should().NotBeNullOrEmpty();
		hash.Should().HaveLength(16);
	}
}
