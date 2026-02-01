// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch.IndexManagement;

namespace Elastic.Integration.Tests.Mapping;

/// <summary>Verifies ApplicationLog data stream mappings and templates.</summary>
public class ApplicationLogMappingTests : IntegrationTestBase
{
	private const string DataStreamName = "logs-ecommerce.app-production";

	[Test]
	public async Task LogDataStream_Exists()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetDataStreamAsync(new GetDataStreamRequest(DataStreamName));

		response.IsValidResponse.Should().BeTrue();
		response.DataStreams.Should().NotBeEmpty();
	}

	[Test]
	public async Task LogDataStream_HasCorrectMappings()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest($"{DataStreamName}*"));

		response.IsValidResponse.Should().BeTrue();
		response.Indices.Should().NotBeEmpty();

		var mappings = response.Indices.First().Value.Mappings;
		var properties = mappings.Properties;
		properties.Should().NotBeNull();

		// Verify key field mappings exist
		properties!.Should().ContainKey("@timestamp");
		properties.Should().ContainKey("message");
	}

	[Test]
	public async Task LogDataStream_HasTimestampField()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest($"{DataStreamName}*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// @timestamp should be date
		properties["@timestamp"].Type.Should().Be("date");
	}

	[Test]
	public async Task LogDataStream_HasKeywordFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest($"{DataStreamName}*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// log.level should be keyword
		properties["log.level"].Type.Should().Be("keyword");

		// service.name should be keyword
		properties["service.name"].Type.Should().Be("keyword");
	}

	[Test]
	public async Task LogDataStream_HasTextFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest($"{DataStreamName}*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// Message should be text
		properties["message"].Type.Should().Be("text");
	}

	[Test]
	public async Task LogDataStream_HasIpField()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest($"{DataStreamName}*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// host.ip should be ip
		properties["host.ip"].Type.Should().Be("ip");
	}

	[Test]
	public async Task LogDataStream_SettingsComponentTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync($"{DataStreamName}-settings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task LogDataStream_MappingsComponentTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync($"{DataStreamName}-mappings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task LogDataStream_IndexTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest(DataStreamName));

		response.IsValidResponse.Should().BeTrue();
		response.IndexTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task LogDataStream_IndexTemplateHasDataStreamConfig()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest(DataStreamName));

		response.IsValidResponse.Should().BeTrue();

		var template = response.IndexTemplates.First();
		template.IndexTemplate.DataStream.Should().NotBeNull();
	}

	[Test]
	public async Task LogDataStream_HasHashMetadata()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest(DataStreamName));

		response.IsValidResponse.Should().BeTrue();

		var template = response.IndexTemplates.First();
		template.IndexTemplate.Meta.Should().NotBeNull();
		template.IndexTemplate.Meta!.Should().ContainKey("hash");
		template.IndexTemplate.Meta!.Should().ContainKey("managed_by");
	}
}
