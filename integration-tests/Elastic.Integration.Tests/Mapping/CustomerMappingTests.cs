// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch.IndexManagement;

namespace Elastic.Integration.Tests.Mapping;

/// <summary>Verifies Customer index mappings and templates.</summary>
public class CustomerMappingTests : IntegrationTestBase
{
	[Test]
	public async Task CustomerIndex_HasCorrectMappings()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("customers*"));

		response.IsValidResponse.Should().BeTrue();
		response.Indices.Should().NotBeEmpty();

		var mappings = response.Indices.First().Value.Mappings;
		var properties = mappings.Properties;
		properties.Should().NotBeNull();

		// Verify key field mappings exist
		properties!.Should().ContainKey("customer_id");
		properties.Should().ContainKey("email");
		properties.Should().ContainKey("first_name");
		properties.Should().ContainKey("last_name");
		properties.Should().ContainKey("tier");
		properties.Should().ContainKey("addresses");
	}

	[Test]
	public async Task CustomerIndex_HasKeywordFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("customers*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// Email should be keyword
		properties["email"].Type.Should().Be("keyword");

		// customer_id should be keyword
		properties["customer_id"].Type.Should().Be("keyword");

		// Tier should be keyword
		properties["tier"].Type.Should().Be("keyword");
	}

	[Test]
	public async Task CustomerIndex_HasTextFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("customers*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// First name should be text
		properties["first_name"].Type.Should().Be("text");

		// Last name should be text
		properties["last_name"].Type.Should().Be("text");
	}

	[Test]
	public async Task CustomerIndex_HasNestedAddresses()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("customers*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// Addresses should be nested
		properties["addresses"].Type.Should().Be("nested");
	}

	[Test]
	public async Task CustomerIndex_HasDateFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("customers*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// CreatedAt should be date
		properties["createdAt"].Type.Should().Be("date");
	}

	[Test]
	public async Task CustomerIndex_SettingsComponentTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync("customers-settings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task CustomerIndex_MappingsComponentTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync("customers-mappings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task CustomerIndex_IndexTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest("customers"));

		response.IsValidResponse.Should().BeTrue();
		response.IndexTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task CustomerIndex_HasHashMetadata()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest("customers"));

		response.IsValidResponse.Should().BeTrue();

		var template = response.IndexTemplates.First();
		template.IndexTemplate.Meta.Should().NotBeNull();
		template.IndexTemplate.Meta!.Should().ContainKey("hash");
		template.IndexTemplate.Meta!.Should().ContainKey("managed_by");
	}
}
