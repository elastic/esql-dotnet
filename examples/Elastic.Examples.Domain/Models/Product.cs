// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;

namespace Elastic.Examples.Domain.Models;

/// <summary>
/// E-commerce product with full-text search, vectors for similarity, and nested categories.
/// Demonstrates traditional index with aliases and complex field types.
/// </summary>
public class Product
{
	[JsonPropertyName("product_id")]
	[Keyword(IgnoreAbove = 64)]
	public string Id { get; set; } = string.Empty;

	[Text(Analyzer = "product_name_analyzer", SearchAnalyzer = "product_name_search_analyzer")]
	public string Name { get; set; } = string.Empty;

	[Text(Analyzer = "product_description_analyzer", Norms = false)]
	public string Description { get; set; } = string.Empty;

	[Keyword(Normalizer = "sku_normalizer")]
	public string Sku { get; set; } = string.Empty;

	[Keyword]
	public string Brand { get; set; } = string.Empty;

	[JsonPropertyName("price_usd")]
	public double Price { get; set; }

	[JsonPropertyName("sale_price_usd")]
	public double? SalePrice { get; set; }

	[JsonPropertyName("in_stock")]
	public bool InStock { get; set; }

	[JsonPropertyName("stock_quantity")]
	public int StockQuantity { get; set; }

	[Date(Format = "strict_date_optional_time")]
	public DateTime CreatedAt { get; set; }

	[Date(Format = "strict_date_optional_time")]
	public DateTime? UpdatedAt { get; set; }

	[Nested]
	public List<ProductCategory> Categories { get; set; } = [];

	[Nested]
	public List<ProductSpec> Specs { get; set; } = [];

	[Object]
	public ProductDimensions? Dimensions { get; set; }

	[Keyword]
	public List<string> Tags { get; set; } = [];

	[DenseVector(Dims = 384, Similarity = "cosine")]
	public float[]? NameEmbedding { get; set; }

	[SemanticText(InferenceId = "product-search-elser")]
	public string? SemanticDescription { get; set; }

	[Completion(Analyzer = "simple")]
	public string? Suggest { get; set; }

	[JsonPropertyName("avg_rating")]
	public double? AverageRating { get; set; }

	[JsonPropertyName("review_count")]
	public int ReviewCount { get; set; }

	[JsonIgnore]
	public string InternalNotes { get; set; } = string.Empty;
}

/// <summary>
/// Product category for nested filtering.
/// </summary>
public class ProductCategory
{
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[Keyword]
	public string Name { get; set; } = string.Empty;

	public int Level { get; set; }

	[Keyword]
	public string? ParentId { get; set; }
}

/// <summary>
/// Dynamic product specifications (size, color, material, etc.).
/// </summary>
public class ProductSpec
{
	[Keyword]
	public string Name { get; set; } = string.Empty;

	[Keyword]
	public string Value { get; set; } = string.Empty;

	[Keyword]
	public string? Unit { get; set; }
}

/// <summary>
/// Product physical dimensions.
/// </summary>
public class ProductDimensions
{
	public double? Width { get; set; }
	public double? Height { get; set; }
	public double? Depth { get; set; }
	public double? Weight { get; set; }

	[Keyword]
	public string? Unit { get; set; }
}
