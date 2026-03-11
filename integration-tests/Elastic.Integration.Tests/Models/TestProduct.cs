// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Integration.Tests.Models;

public class TestProduct
{
	[JsonPropertyName("product_id")]
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Brand { get; set; } = string.Empty;

	[JsonPropertyName("price_usd")]
	public double Price { get; set; }

	[JsonPropertyName("sale_price_usd")]
	public double? SalePrice { get; set; }

	[JsonPropertyName("in_stock")]
	public bool InStock { get; set; }

	[JsonPropertyName("stock_quantity")]
	public int StockQuantity { get; set; }

	public ProductCategory Category { get; set; }

	[JsonPropertyName("category_id")]
	public string CategoryId { get; set; } = string.Empty;

	[JsonPropertyName("created_at")]
	public DateTime CreatedAt { get; set; }

	public List<string> Tags { get; set; } = [];
}

public enum ProductCategory
{
	Electronics,
	Clothing,
	Books,
	Home,
	Sports
}

public class RawProductSummary
{
	[JsonPropertyName("product_id")]
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;
}
