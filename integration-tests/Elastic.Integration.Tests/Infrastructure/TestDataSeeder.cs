// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Esql.Integration.Tests.Models;
using Elastic.Esql.Vectors;

namespace Elastic.Esql.Integration.Tests.Infrastructure;

public static class TestDataSeeder
{
	public const string ProductIndex = "test-products";
	public const string OrderIndex = "test-orders";
	public const string EventIndex = "test-events";
	public const string CategoryLookupIndex = "test-categories";
	public const string CategoryOverlapIndex = "test-category-overlap";
	public const string UserProfileIndex = "test-user-profiles";
	public const string BookIndex = "test-books";

	public static IReadOnlyList<TestProduct> Products { get; } = CreateProducts();
	public static IReadOnlyList<TestOrder> Orders { get; } = CreateOrders();
	public static IReadOnlyList<TestEvent> Events { get; } = CreateEvents();
	public static IReadOnlyList<TestCategoryLookup> CategoryLookups { get; } = CreateCategoryLookups();
	public static IReadOnlyList<TestCategoryOverlap> CategoryOverlaps { get; } = CreateCategoryOverlaps();
	public static IReadOnlyList<TestUserProfile> UserProfiles { get; } = CreateUserProfiles();
	public static IReadOnlyList<TestBook> Books { get; } = CreateBooks();

	public static async Task SeedAllAsync(ElasticsearchClient client, CancellationToken ct = default)
	{
		await EnsureTrialLicenseAsync(client, ct).ConfigureAwait(false);

		await SeedProductsAsync(client, ct).ConfigureAwait(false);
		await SeedOrdersAsync(client, ct).ConfigureAwait(false);
		await SeedEventsAsync(client, ct).ConfigureAwait(false);
		await SeedCategoryLookupAsync(client, ct).ConfigureAwait(false);
		await SeedCategoryOverlapAsync(client, ct).ConfigureAwait(false);
		await SeedUserProfilesAsync(client, ct).ConfigureAwait(false);
		await SeedBooksAsync(client, ct).ConfigureAwait(false);

		await client.Indices.RefreshAsync(Indices.All, ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Activates a trial license on the cluster so that platinum/enterprise features (e.g. FUSE)
	/// can be exercised by integration tests. No-op when the license is already trial / platinum /
	/// enterprise. Idempotent across runs against the same external cluster.
	/// </summary>
	private static async Task EnsureTrialLicenseAsync(ElasticsearchClient client, CancellationToken ct)
	{
		var licenseInfo = await client.LicenseManagement.GetAsync(ct).ConfigureAwait(false);
		if (licenseInfo.IsValidResponse && licenseInfo.License?.Type is { } type)
		{
			var typeName = type.ToString().ToLowerInvariant();
			if (typeName is "trial" or "platinum" or "enterprise")
				return;
		}

		_ = await client.LicenseManagement.PostStartTrialAsync(p => p.Acknowledge(true), ct).ConfigureAwait(false);
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

	private static async Task SeedUserProfilesAsync(ElasticsearchClient client, CancellationToken ct)
	{
		await client.Indices.CreateAsync(UserProfileIndex, i => i
			.Settings(s => s.NumberOfShards(1).NumberOfReplicas(0))
			.Mappings(m => m
				.Properties(p => p
					.Keyword("user_id")
					.Keyword("name")
					.Object("address", o => o
						.Properties(ap => ap
							.Keyword("street")
							.Keyword("city")
							.Keyword("country")
						)
					)
				)
			), ct).ConfigureAwait(false);

		var response = await client.BulkAsync(b => b.Index(UserProfileIndex).IndexMany(UserProfiles), ct).ConfigureAwait(false);
		if (response.Errors)
			throw new InvalidOperationException($"Bulk index user profiles failed: {response.DebugInformation}");
	}

	private static async Task SeedBooksAsync(ElasticsearchClient client, CancellationToken ct)
	{
		await client.Indices.CreateAsync(BookIndex, i => i
			.Settings(s => s.NumberOfShards(1).NumberOfReplicas(0))
			.Mappings(m => m
				.Properties<TestBook>(p => p
					.Keyword(b => b.Id)
					.Text(b => b.Title, t => t.Fields(f => f.Keyword("keyword")))
					.Text(b => b.Description)
					.DenseVector("title_vec", v => v
						.Dims(4)
						.Similarity(DenseVectorSimilarity.Cosine))
					.DenseVector("rgb_vector", v => v
						.Dims(3)
						.ElementType(DenseVectorElementType.Byte)
						.Similarity(DenseVectorSimilarity.L2Norm))
				)
			), ct).ConfigureAwait(false);

		var response = await client.BulkAsync(b => b.Index(BookIndex).IndexMany(Books), ct).ConfigureAwait(false);
		if (response.Errors)
			throw new InvalidOperationException($"Bulk index books failed: {response.DebugInformation}");
	}

	/// <summary>
	/// Twelve hand-picked books with deterministic 4-dim float vectors and 3-dim byte (RGB) vectors.
	/// The vectors are arranged so that KNN ordering and V_* similarity scores are reproducible:
	/// books 1-3 cluster around the unit-x axis, 4-6 around unit-y, 7-9 around unit-z, 10-12 around (1,1,1)/sqrt(3).
	/// </summary>
	private static IReadOnlyList<TestBook> CreateBooks() =>
	[
		new() { Id = "book-01", Title = "Programming Patterns", Description = "Software design patterns and best practices.", TitleVec = new float[] { 1.00f, 0.00f, 0.00f, 0.00f }, RgbVector = new byte[] { 255, 0, 0 } },
		new() { Id = "book-02", Title = "Clean Programming", Description = "Writing readable maintainable code.", TitleVec = new float[] { 0.95f, 0.10f, 0.00f, 0.00f }, RgbVector = new byte[] { 200, 20, 0 } },
		new() { Id = "book-03", Title = "Programming in Practice", Description = "Practical software engineering.", TitleVec = new float[] { 0.90f, 0.20f, 0.05f, 0.00f }, RgbVector = new byte[] { 180, 40, 10 } },
		new() { Id = "book-04", Title = "Cooking with Vegetables", Description = "Healthy plant-based recipes.", TitleVec = new float[] { 0.00f, 1.00f, 0.00f, 0.00f }, RgbVector = new byte[] { 0, 255, 0 } },
		new() { Id = "book-05", Title = "Vegetarian Curry Recipes", Description = "Spicy curries from around the world.", TitleVec = new float[] { 0.10f, 0.90f, 0.10f, 0.00f }, RgbVector = new byte[] { 20, 200, 20 } },
		new() { Id = "book-06", Title = "The Vegetable Cookbook", Description = "Simple weeknight vegetable dinners.", TitleVec = new float[] { 0.05f, 0.95f, 0.00f, 0.00f }, RgbVector = new byte[] { 10, 220, 0 } },
		new() { Id = "book-07", Title = "Shakespeare on Stage", Description = "Performing Shakespeare in modern theatre.", TitleVec = new float[] { 0.00f, 0.00f, 1.00f, 0.00f }, RgbVector = new byte[] { 0, 0, 255 } },
		new() { Id = "book-08", Title = "Shakespeare for Programmers", Description = "Cross-disciplinary essays on language design and the bard.", TitleVec = new float[] { 0.50f, 0.00f, 0.85f, 0.00f }, RgbVector = new byte[] { 100, 0, 200 } },
		new() { Id = "book-09", Title = "The Complete Shakespeare", Description = "All plays and sonnets in one volume.", TitleVec = new float[] { 0.10f, 0.10f, 0.95f, 0.00f }, RgbVector = new byte[] { 20, 20, 220 } },
		new() { Id = "book-10", Title = "Mixed Topics Anthology", Description = "Essays spanning many subjects.", TitleVec = new float[] { 0.50f, 0.50f, 0.50f, 0.50f }, RgbVector = new byte[] { 128, 128, 128 } },
		new() { Id = "book-11", Title = "Generalist Knowledge", Description = "A primer on broad subject expertise.", TitleVec = new float[] { 0.40f, 0.40f, 0.40f, 0.40f }, RgbVector = new byte[] { 100, 100, 100 } },
		new() { Id = "book-12", Title = "Outlier Volume", Description = "Niche material on obscure topics.", TitleVec = new float[] { 0.00f, 0.00f, 0.00f, 1.00f }, RgbVector = new byte[] { 0, 0, 0 } }
	];

	private static IReadOnlyList<TestUserProfile> CreateUserProfiles()
	{
		var cities = new[] { "New York", "London", "Tokyo", "Berlin", "Sydney" };
		var countries = new[] { "US", "UK", "JP", "DE", "AU" };
		var streets = new[] { "1st Ave", "Baker St", "Shibuya", "Unter den Linden", "George St" };
		var profiles = new List<TestUserProfile>();

		for (var i = 1; i <= 10; i++)
		{
			var idx = (i - 1) % cities.Length;
			profiles.Add(new TestUserProfile
			{
				UserId = $"user-{i:D4}",
				Name = $"User {i}",
				Address = i % 3 == 0
					? null
					: new TestAddress
					{
						Street = streets[idx],
						City = cities[idx],
						Country = countries[idx]
					}
			});
		}

		return profiles;
	}
}
