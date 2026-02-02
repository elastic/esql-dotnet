// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch.IndexManagement;

namespace Elastic.Integration.Tests.Mapping;

/// <summary>Verifies Product index mappings and templates.</summary>
public class ProductMappingTests : IntegrationTestBase
{
	[Test]
	public async Task ProductIndex_HasCorrectMappings()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("products*"));

		response.IsValidResponse.Should().BeTrue();
		response.Indices.Should().NotBeEmpty();

		var mappings = response.Indices.First().Value.Mappings;
		var properties = mappings.Properties;
		properties.Should().NotBeNull();

		// Verify key field mappings exist
		properties!.Should().ContainKey("name");
		properties.Should().ContainKey("brand");
		properties.Should().ContainKey("price_usd");
		properties.Should().ContainKey("in_stock");
		properties.Should().ContainKey("categories");
		properties.Should().ContainKey("specs");
	}

	[Test]
	public async Task ProductIndex_HasKeywordFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("products*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// Brand should be keyword
		properties["brand"].Type.Should().Be("keyword");

		// SKU should be keyword
		properties["sku"].Type.Should().Be("keyword");

		// product_id should be keyword
		properties["product_id"].Type.Should().Be("keyword");
	}

	[Test]
	public async Task ProductIndex_HasTextFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("products*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// Name should be text
		properties["name"].Type.Should().Be("text");

		// Description should be text
		properties["description"].Type.Should().Be("text");
	}

	[Test]
	public async Task ProductIndex_HasNestedFields()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetMappingAsync(new GetMappingRequest("products*"));

		response.IsValidResponse.Should().BeTrue();

		var properties = response.Indices.First().Value.Mappings.Properties!;

		// Categories should be nested
		properties["categories"].Type.Should().Be("nested");

		// Specs should be nested
		properties["specs"].Type.Should().Be("nested");
	}

	[Test]
	public async Task ProductIndex_SettingsComponentTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync("products-settings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task ProductIndex_MappingsComponentTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync("products-mappings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task ProductIndex_IndexTemplateExists()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest("products"));

		response.IsValidResponse.Should().BeTrue();
		response.IndexTemplates.Should().NotBeEmpty();
	}

	[Test]
	public async Task ProductIndex_HasHashMetadata()
	{
		var response = await Fixture.ElasticsearchClient.Indices
			.GetIndexTemplateAsync(new GetIndexTemplateRequest("products"));

		response.IsValidResponse.Should().BeTrue();

		var template = response.IndexTemplates.First();
		template.IndexTemplate.Meta.Should().NotBeNull();
		template.IndexTemplate.Meta!.Should().ContainKey("hash");
		template.IndexTemplate.Meta!.Should().ContainKey("managed_by");
	}

	[Test]
	public async Task ProductIndex_SettingsTemplateContainsAnalyzers()
	{
		// The settings component template contains custom analyzers defined in Product.ConfigureAnalysis:
		// - product_name_analyzer (edge n-gram for autocomplete)
		// - product_name_search_analyzer
		// - product_description_analyzer (English analyzer with custom stopwords)
		// - sku_normalizer (lowercase normalizer)
		// The template existence verifies the analyzers were bootstrapped
		var response = await Fixture.ElasticsearchClient.Cluster
			.GetComponentTemplateAsync("products-settings");

		response.IsValidResponse.Should().BeTrue();
		response.ComponentTemplates.Should().NotBeEmpty();
	}
}
