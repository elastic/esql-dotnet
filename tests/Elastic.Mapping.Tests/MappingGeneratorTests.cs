// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

public class MappingGeneratorTests
{
	[Test]
	public void Index_GeneratesStaticMappingClass()
	{
		// The ElasticsearchContext class should be generated
		var hash = LogEntry.ElasticsearchContext.Hash;
		hash.Should().NotBeNullOrEmpty();
		hash.Should().HaveLength(16);
	}

	[Test]
	public void Index_GeneratesFieldConstants()
	{
		// Field constants should be generated from JsonPropertyName or camelCase
		LogEntry.ElasticsearchContext.Fields.Timestamp.Should().Be("@timestamp");
		LogEntry.ElasticsearchContext.Fields.Level.Should().Be("log.level");
		LogEntry.ElasticsearchContext.Fields.Message.Should().Be("message");
		LogEntry.ElasticsearchContext.Fields.StatusCode.Should().Be("statusCode");
	}

	[Test]
	public void Index_GeneratesIndexStrategy()
	{
		var strategy = LogEntry.ElasticsearchContext.IndexStrategy;
		strategy.WriteTarget.Should().Be("logs-write");
	}

	[Test]
	public void Index_GeneratesSearchStrategy()
	{
		var strategy = LogEntry.ElasticsearchContext.SearchStrategy;
		strategy.Pattern.Should().Be("logs-*");
		strategy.ReadAlias.Should().Be("logs-read");
	}

	[Test]
	public void Index_GeneratesSettingsJson()
	{
		var json = LogEntry.ElasticsearchContext.GetSettingsJson();
		json.Should().Contain("\"number_of_shards\": 3");
		json.Should().Contain("\"number_of_replicas\": 2");
	}

	[Test]
	public void Index_GeneratesMappingJson()
	{
		var json = LogEntry.ElasticsearchContext.GetMappingJson();
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
		var json = LogEntry.ElasticsearchContext.GetIndexJson();
		json.Should().Contain("\"settings\":");
		json.Should().Contain("\"mappings\":");
	}

	[Test]
	public void Index_HashIsStable()
	{
		// Hash should be deterministic
		var hash1 = LogEntry.ElasticsearchContext.Hash;
		var hash2 = LogEntry.ElasticsearchContext.Hash;
		hash1.Should().Be(hash2);
	}

	[Test]
	public void Index_SeparateHashesProvided()
	{
		// Separate hashes for settings and mappings
		var settingsHash = LogEntry.ElasticsearchContext.SettingsHash;
		var mappingsHash = LogEntry.ElasticsearchContext.MappingsHash;
		var combinedHash = LogEntry.ElasticsearchContext.Hash;

		settingsHash.Should().NotBeNullOrEmpty();
		mappingsHash.Should().NotBeNullOrEmpty();
		settingsHash.Should().NotBe(mappingsHash);
	}

	[Test]
	public void DataStream_GeneratesCorrectStrategy()
	{
		var indexStrategy = NginxAccessLog.ElasticsearchContext.IndexStrategy;
		indexStrategy.DataStreamName.Should().Be("logs-nginx.access-production");
		indexStrategy.Type.Should().Be("logs");
		indexStrategy.Dataset.Should().Be("nginx.access");
		indexStrategy.Namespace.Should().Be("production");

		var searchStrategy = NginxAccessLog.ElasticsearchContext.SearchStrategy;
		searchStrategy.Pattern.Should().Be("logs-nginx.access-*");
	}

	[Test]
	public void SimpleDocument_InfersTypesFromClrTypes()
	{
		var json = SimpleDocument.ElasticsearchContext.GetMappingJson();
		json.Should().Contain("\"name\": { \"type\": \"keyword\"");
		json.Should().Contain("\"value\": { \"type\": \"integer\"");
		json.Should().Contain("\"createdAt\": { \"type\": \"date\"");
	}

	[Test]
	public void AdvancedDocument_SupportsSpecializedTypes()
	{
		var json = AdvancedDocument.ElasticsearchContext.GetMappingJson();
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

	[Test]
	public void Index_GeneratesFieldMappingDictionaries()
	{
		// PropertyToField maps C# property names to ES|QL field names
		var propertyToField = LogEntry.ElasticsearchContext.FieldMapping.PropertyToField;
		propertyToField["Timestamp"].Should().Be("@timestamp");
		propertyToField["Level"].Should().Be("log.level");
		propertyToField["Message"].Should().Be("message");
		propertyToField["StatusCode"].Should().Be("statusCode");

		// FieldToProperty maps ES|QL field names to C# property names
		var fieldToProperty = LogEntry.ElasticsearchContext.FieldMapping.FieldToProperty;
		fieldToProperty["@timestamp"].Should().Be("Timestamp");
		fieldToProperty["log.level"].Should().Be("Level");
		fieldToProperty["message"].Should().Be("Message");
		fieldToProperty["statusCode"].Should().Be("StatusCode");
	}

	[Test]
	public void Index_GeneratesIgnoredPropertiesSet()
	{
		// InternalId has [JsonIgnore] and should be in IgnoredProperties
		var ignoredProperties = LogEntry.ElasticsearchContext.IgnoredProperties;
		ignoredProperties.Should().Contain("InternalId");
		ignoredProperties.Should().NotContain("Timestamp");
		ignoredProperties.Should().NotContain("Message");
	}

	[Test]
	public void Index_GeneratesGetPropertyMap()
	{
		// GetPropertyMap returns a dictionary for deserialization
		var propertyMap = LogEntry.ElasticsearchContext.GetPropertyMap();

		// Should map by field name
		propertyMap["@timestamp"].Name.Should().Be("Timestamp");
		propertyMap["log.level"].Name.Should().Be("Level");
		propertyMap["message"].Name.Should().Be("Message");

		// Should also map by property name
		propertyMap["Timestamp"].Name.Should().Be("Timestamp");
		propertyMap["Level"].Name.Should().Be("Level");

		// Ignored properties should not be in the map
		propertyMap.Should().NotContainKey("InternalId");
		propertyMap.Should().NotContainKey("internalId");
	}

	[Test]
	public void SimpleDocument_GeneratesEmptyIgnoredPropertiesSet()
	{
		// SimpleDocument has no [JsonIgnore] properties
		var ignoredProperties = SimpleDocument.ElasticsearchContext.IgnoredProperties;
		ignoredProperties.Should().BeEmpty();
	}
}
