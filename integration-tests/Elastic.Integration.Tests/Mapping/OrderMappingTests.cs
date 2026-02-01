// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch.IndexManagement;

namespace Elastic.Integration.Tests.Mapping;

/// <summary>Verifies Order index mappings and templates.</summary>
public class OrderMappingTests : IntegrationTestBase
{
	[Test]
	public async Task OrderIndex_HasCorrectMappings()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("orders-*"));

		response.IsValidResponse.Should().BeTrue();
		response.Indices.Should().NotBeEmpty();

		var mappings = response.Indices.First().Value.Mappings;
		var properties = mappings.Properties;
		properties.Should().NotBeNull();

		// Verify key field mappings exist
		properties!.Should().ContainKey("order_id");
		properties.Should().ContainKey("customer_id");
		properties.Should().ContainKey("@timestamp");
		properties.Should().ContainKey("status");
		properties.Should().ContainKey("total_amount");
		properties.Should().ContainKey("items");
	}

	[Test]
	public async Task OrderIndex_HasKeywordFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("orders-*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// order_id should be keyword
		properties["order_id"].Type.Should().Be("keyword");

		// customer_id should be keyword
		properties["customer_id"].Type.Should().Be("keyword");

		// Status should be keyword
		properties["status"].Type.Should().Be("keyword");

		// Currency should be keyword
		properties["currency"].Type.Should().Be("keyword");
	}

	[Test]
	public async Task OrderIndex_HasNestedItems()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("orders-*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// Items should be nested
		properties["items"].Type.Should().Be("nested");
	}

	[Test]
	public async Task OrderIndex_HasGeoPointField()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("orders-*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// DeliveryLocation should be geo_point
		properties["deliveryLocation"].Type.Should().Be("geo_point");
	}

	[Test]
	public async Task OrderIndex_HasIpField()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("orders-*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// CustomerIp should be ip
		properties["customerIp"].Type.Should().Be("ip");
	}

	[Test]
	public async Task OrderIndex_HasDateFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("orders-*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// @timestamp should be date
		properties["@timestamp"].Type.Should().Be("date");
	}

	[Test]
	public async Task OrderIndex_SettingsComponentTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync("orders-write-settings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task OrderIndex_MappingsComponentTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync("orders-write-mappings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task OrderIndex_IndexTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest("orders-write"));

		response.IsValidResponse.Should().BeTrue();
		response.IndexTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task OrderIndex_HasHashMetadata()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest("orders-write"));

		response.IsValidResponse.Should().BeTrue();

		var template = response.IndexTemplates.First();
		template.IndexTemplate.Meta.Should().NotBeNull();
		template.IndexTemplate.Meta!.Should().ContainKey("hash");
		template.IndexTemplate.Meta!.Should().ContainKey("managed_by");
	}
}
