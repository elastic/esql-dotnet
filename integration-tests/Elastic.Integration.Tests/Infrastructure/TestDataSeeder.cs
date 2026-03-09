// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Infrastructure;

public static class TestDataSeeder
{
	public const string ProductIndex = "test-products";
	public const string OrderIndex = "test-orders";
	public const string EventIndex = "test-events";
	public const string CategoryLookupIndex = "test-categories";
	public const string CategoryOverlapIndex = "test-category-overlap";

	public static IReadOnlyList<TestProduct> Products { get; } = CreateProducts();
	public static IReadOnlyList<TestOrder> Orders { get; } = CreateOrders();
	public static IReadOnlyList<TestEvent> Events { get; } = CreateEvents();
	public static IReadOnlyList<TestCategoryLookup> CategoryLookups { get; } = CreateCategoryLookups();
	public static IReadOnlyList<TestCategoryOverlap> CategoryOverlaps { get; } = CreateCategoryOverlaps();

	public static async Task SeedAllAsync(ElasticsearchClient client, CancellationToken ct = default)
	{
		await SeedProductsAsync(client, ct).ConfigureAwait(false);
		await SeedOrdersAsync(client, ct).ConfigureAwait(false);
		await SeedEventsAsync(client, ct).ConfigureAwait(false);
		await SeedCategoryLookupAsync(client, ct).ConfigureAwait(false);
		await SeedCategoryOverlapAsync(client, ct).ConfigureAwait(false);

		await client.Indices.RefreshAsync(Indices.All, ct).ConfigureAwait(false);
	}

	private static async Task SeedProductsAsync(ElasticsearchClient client, CancellationToken ct)
	{
		await client.Indices.CreateAsync(ProductIndex, i => i
			.Settings(s => s.NumberOfShards(1).NumberOfReplicas(0)), ct).ConfigureAwait(false);

		var response = await client.BulkAsync(b => b.Index(ProductIndex).IndexMany(Products), ct).ConfigureAwait(false);
		if (response.Errors)
			throw new InvalidOperationException($"Bulk index products failed: {response.DebugInformation}");
	}

	private static async Task SeedOrdersAsync(ElasticsearchClient client, CancellationToken ct)
	{
		await client.Indices.CreateAsync(OrderIndex, i => i
			.Settings(s => s.NumberOfShards(1).NumberOfReplicas(0)), ct).ConfigureAwait(false);

		var response = await client.BulkAsync(b => b.Index(OrderIndex).IndexMany(Orders), ct).ConfigureAwait(false);
		if (response.Errors)
			throw new InvalidOperationException($"Bulk index orders failed: {response.DebugInformation}");
	}

	private static async Task SeedEventsAsync(ElasticsearchClient client, CancellationToken ct)
	{
		await client.Indices.CreateAsync(EventIndex, i => i
			.Settings(s => s.NumberOfShards(1).NumberOfReplicas(0)), ct).ConfigureAwait(false);

		var response = await client.BulkAsync(b => b.Index(EventIndex).IndexMany(Events), ct).ConfigureAwait(false);
		if (response.Errors)
			throw new InvalidOperationException($"Bulk index events failed: {response.DebugInformation}");
	}

	private static async Task SeedCategoryLookupAsync(ElasticsearchClient client, CancellationToken ct)
	{
		await client.Indices.CreateAsync(CategoryLookupIndex, i => i
			.Settings(s => s.NumberOfShards(1).NumberOfReplicas(0).Mode("lookup"))
			.Mappings(m => m
				.Properties(p => p
					.Keyword("category_id")
					.Keyword("category_label")
					.Keyword("region")
				)
			), ct).ConfigureAwait(false);

		var response = await client.BulkAsync(b => b.Index(CategoryLookupIndex).IndexMany(CategoryLookups), ct).ConfigureAwait(false);
		if (response.Errors)
			throw new InvalidOperationException($"Bulk index category lookups failed: {response.DebugInformation}");
	}

	private static async Task SeedCategoryOverlapAsync(ElasticsearchClient client, CancellationToken ct)
	{
		await client.Indices.CreateAsync(CategoryOverlapIndex, i => i
			.Settings(s => s.NumberOfShards(1).NumberOfReplicas(0).Mode("lookup"))
			.Mappings(m => m
				.Properties(p => p
					.Keyword("category_id")
					.Keyword("name")
					.Keyword("region")
				)
			), ct).ConfigureAwait(false);

		var response = await client.BulkAsync(b => b.Index(CategoryOverlapIndex).IndexMany(CategoryOverlaps), ct).ConfigureAwait(false);
		if (response.Errors)
			throw new InvalidOperationException($"Bulk index category overlaps failed: {response.DebugInformation}");
	}

	// =========================================================================
	// Data generation -- deterministic, no external dependencies
	// =========================================================================

	private static IReadOnlyList<TestProduct> CreateProducts()
	{
		var brands = new[] { "TechCorp", "StyleMax", "HomeGoods", "SportPro", "BookWorld" };
		var categories = Enum.GetValues<ProductCategory>();
		var categoryIds = new[] { "cat-electronics", "cat-clothing", "cat-books", "cat-home", "cat-sports" };
		var products = new List<TestProduct>();

		for (var i = 1; i <= 100; i++)
		{
			var catIndex = (i - 1) % categories.Length;
			products.Add(new TestProduct
			{
				Id = $"prod-{i:D4}",
				Name = $"Product {i}",
				Brand = brands[(i - 1) % brands.Length],
				Price = 10.0 + (i * 7.5 % 990),
				SalePrice = i % 3 == 0 ? 5.0 + (i * 3.3 % 500) : null,
				InStock = i % 4 != 0,
				StockQuantity = i % 4 != 0 ? i * 13 % 500 : 0,
				Category = categories[catIndex],
				CategoryId = categoryIds[catIndex],
				CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
				Tags = i % 5 == 0 ? ["sale", "featured"] : i % 3 == 0 ? ["new"] : []
			});
		}

		return products;
	}

	private static IReadOnlyList<TestOrder> CreateOrders()
	{
		var statuses = Enum.GetValues<OrderStatus>();
		var currencies = new[] { "USD", "EUR", "GBP" };
		var ips = new[] { "192.168.1.1", "10.0.0.5", "172.16.0.100", null, "203.0.113.42" };
		var orders = new List<TestOrder>();

		for (var i = 1; i <= 100; i++)
		{
			orders.Add(new TestOrder
			{
				Id = $"order-{i:D4}",
				CustomerId = $"cust-{(i % 20) + 1:D4}",
				Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i * 3),
				Status = statuses[(i - 1) % statuses.Length],
				TotalAmount = 25.50m + (i * 13.7m % 975m),
				Currency = currencies[(i - 1) % currencies.Length],
				ClientIp = ips[(i - 1) % ips.Length],
				DiscountPercent = i % 4 == 0 ? i * 2.5 % 30 : null,
				PromoCodes = i % 7 == 0 ? ["SAVE10", "WELCOME"] : i % 5 == 0 ? ["FIRST"] : [],
				Notes = i % 6 == 0 ? $"Note for order {i}" : null
			});
		}

		return orders;
	}

	private static IReadOnlyList<TestEvent> CreateEvents()
	{
		var levels = new[] { "Info", "Warn", "Error", "Debug" };
		var services = new[] { "api-gateway", "order-service", "payment-service", "user-service" };
		var events = new List<TestEvent>();

		for (var i = 1; i <= 100; i++)
		{
			events.Add(new TestEvent
			{
				Timestamp = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 15),
				Level = levels[(i - 1) % levels.Length],
				ServiceName = services[(i - 1) % services.Length],
				Message = $"Event message {i}: operation completed",
				HttpStatusCode = i % 5 == 0 ? 500 : i % 3 == 0 ? 404 : 200,
				DurationNanos = i % 4 == 0 ? null : i * 1_500_000L,
				HostIp = i % 2 == 0 ? "10.0.0.1" : "10.0.0.2"
			});
		}

		return events;
	}

	private static IReadOnlyList<TestCategoryLookup> CreateCategoryLookups() =>
	[
		new() { CategoryId = "cat-electronics", CategoryLabel = "Electronics & Gadgets", Region = "Global" },
		new() { CategoryId = "cat-clothing", CategoryLabel = "Fashion & Apparel", Region = "US" },
		new() { CategoryId = "cat-books", CategoryLabel = "Books & Media", Region = "Global" },
		new() { CategoryId = "cat-home", CategoryLabel = "Home & Garden", Region = "EU" },
		new() { CategoryId = "cat-sports", CategoryLabel = "Sports & Outdoors", Region = "US" }
	];

	private static IReadOnlyList<TestCategoryOverlap> CreateCategoryOverlaps() =>
	[
		new() { CategoryId = "cat-electronics", Name = "Gadgets", Region = "Global" },
		new() { CategoryId = "cat-clothing", Name = "Apparel", Region = "US" },
		new() { CategoryId = "cat-books", Name = "Media", Region = "Global" },
		new() { CategoryId = "cat-home", Name = "Garden", Region = "EU" },
		new() { CategoryId = "cat-sports", Name = "Outdoors", Region = "US" }
	];
}
