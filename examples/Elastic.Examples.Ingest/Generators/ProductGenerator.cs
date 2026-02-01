// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Bogus;
using Elastic.Examples.Domain.Models;

namespace Elastic.Examples.Ingest.Generators;

/// <summary>Generates fake Product data for testing and demos.</summary>
public static class ProductGenerator
{
	private const int Seed = 12345;

	private static readonly string[] Categories =
	[
		"Electronics", "Clothing", "Home & Garden", "Sports", "Books",
		"Toys", "Beauty", "Automotive", "Food", "Health"
	];

	private static readonly string[] SubCategories =
	[
		"Accessories", "Premium", "Budget", "Clearance", "New Arrivals",
		"Best Sellers", "Featured", "Limited Edition"
	];

	private static readonly string[] Brands =
	[
		"TechCorp", "StyleMax", "HomeEase", "SportPro", "BookWorm",
		"KidPlay", "GlowUp", "AutoParts", "FreshBite", "WellLife"
	];

	private static readonly string[] Units = ["cm", "in", "mm"];

	public static IReadOnlyList<Product> Generate(int count = 1000)
	{
		Randomizer.Seed = new Random(Seed);

		var categoryFaker = new Faker<ProductCategory>()
			.RuleFor(c => c.Id, f => f.Random.Guid().ToString("N")[..8])
			.RuleFor(c => c.Name, f => f.PickRandom(Categories))
			.RuleFor(c => c.Level, f => f.Random.Int(1, 3))
			.RuleFor(c => c.ParentId, (f, c) => c.Level > 1 ? f.Random.Guid().ToString("N")[..8] : null);

		var specFaker = new Faker<ProductSpec>()
			.RuleFor(s => s.Name, f => f.PickRandom("Color", "Size", "Material", "Weight", "Capacity"))
			.RuleFor(s => s.Value, f => f.Commerce.Color())
			.RuleFor(s => s.Unit, f => f.Random.Bool(0.3f) ? f.PickRandom(Units) : null);

		var dimensionsFaker = new Faker<ProductDimensions>()
			.RuleFor(d => d.Width, f => f.Random.Double(1, 100))
			.RuleFor(d => d.Height, f => f.Random.Double(1, 100))
			.RuleFor(d => d.Depth, f => f.Random.Double(1, 50))
			.RuleFor(d => d.Weight, f => f.Random.Double(0.1, 50))
			.RuleFor(d => d.Unit, f => f.PickRandom(Units));

		var productFaker = new Faker<Product>()
			.RuleFor(p => p.Id, f => f.Random.Guid().ToString("N"))
			.RuleFor(p => p.Name, f => f.Commerce.ProductName())
			.RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
			.RuleFor(p => p.Sku, f => f.Commerce.Ean13())
			.RuleFor(p => p.Brand, f => f.PickRandom(Brands))
			.RuleFor(p => p.Price, f => Math.Round(f.Random.Double(9.99, 999.99), 2))
			.RuleFor(p => p.SalePrice, (f, p) => f.Random.Bool(0.3f) ? Math.Round(p.Price * f.Random.Double(0.5, 0.9), 2) : null)
			.RuleFor(p => p.InStock, f => f.Random.Bool(0.85f))
			.RuleFor(p => p.StockQuantity, (f, p) => p.InStock ? f.Random.Int(1, 500) : 0)
			.RuleFor(p => p.CreatedAt, f => f.Date.Past(2))
			.RuleFor(p => p.UpdatedAt, (f, p) => f.Random.Bool(0.6f) ? f.Date.Between(p.CreatedAt, DateTime.UtcNow) : null)
			.RuleFor(p => p.Categories, f => categoryFaker.Generate(f.Random.Int(1, 3)))
			.RuleFor(p => p.Specs, f => specFaker.Generate(f.Random.Int(2, 5)))
			.RuleFor(p => p.Dimensions, f => f.Random.Bool(0.7f) ? dimensionsFaker.Generate() : null)
			.RuleFor(p => p.Tags, f => f.Make(f.Random.Int(1, 5), () => f.PickRandom(SubCategories)))
			.RuleFor(p => p.AverageRating, f => f.Random.Bool(0.8f) ? Math.Round(f.Random.Double(1, 5), 1) : null)
			.RuleFor(p => p.ReviewCount, (f, p) => p.AverageRating.HasValue ? f.Random.Int(0, 1000) : 0)
			.RuleFor(p => p.Suggest, (_, p) => p.Name);

		return productFaker.Generate(count);
	}
}
