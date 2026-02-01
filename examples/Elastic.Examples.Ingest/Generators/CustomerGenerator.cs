// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Bogus;
using Elastic.Examples.Domain.Models;

namespace Elastic.Examples.Ingest.Generators;

/// <summary>Generates fake Customer data for testing and demos.</summary>
public static class CustomerGenerator
{
	private const int Seed = 12345;

	private static readonly string[] Languages = ["en", "es", "fr", "de", "pt", "zh", "ja"];
	private static readonly string[] Currencies = ["USD", "EUR", "GBP", "CAD", "AUD", "JPY"];
	private static readonly string[] TimeZones = ["America/New_York", "America/Los_Angeles", "Europe/London", "Europe/Paris", "Asia/Tokyo"];
	private static readonly string[] Categories = ["Electronics", "Clothing", "Home", "Sports", "Books"];
	private static readonly string[] Brands = ["TechCorp", "StyleMax", "HomeEase", "SportPro", "BookWorm"];

	public static IReadOnlyList<Customer> Generate(int count = 500)
	{
		Randomizer.Seed = new Random(Seed);

		var addressFaker = new Faker<Address>()
			.RuleFor(a => a.Street, f => f.Address.StreetAddress())
			.RuleFor(a => a.City, f => f.Address.City())
			.RuleFor(a => a.State, f => f.Address.StateAbbr())
			.RuleFor(a => a.PostalCode, f => f.Address.ZipCode())
			.RuleFor(a => a.Country, f => f.Address.CountryCode());

		var preferencesFaker = new Faker<CustomerPreferences>()
			.RuleFor(p => p.PreferredLanguage, f => f.PickRandom(Languages))
			.RuleFor(p => p.PreferredCurrency, f => f.PickRandom(Currencies))
			.RuleFor(p => p.TimeZone, f => f.PickRandom(TimeZones))
			.RuleFor(p => p.FavoriteCategories, f => f.Make(f.Random.Int(1, 3), () => f.PickRandom(Categories)))
			.RuleFor(p => p.FavoriteBrands, f => f.Make(f.Random.Int(0, 3), () => f.PickRandom(Brands)))
			.RuleFor(p => p.EmailNotifications, f => f.Random.Bool(0.8f))
			.RuleFor(p => p.SmsNotifications, f => f.Random.Bool(0.3f))
			.RuleFor(p => p.PushNotifications, f => f.Random.Bool(0.6f));

		var analyticsFaker = new Faker<CustomerAnalytics>()
			.RuleFor(a => a.TotalOrders, f => f.Random.Int(0, 100))
			.RuleFor(a => a.TotalSpent, (f, a) => a.TotalOrders * f.Random.Decimal(20, 200))
			.RuleFor(a => a.AverageOrderValue, (f, a) => a.TotalOrders > 0 ? a.TotalSpent / a.TotalOrders : 0)
			.RuleFor(a => a.DaysSinceLastOrder, f => f.Random.Bool(0.8f) ? f.Random.Int(1, 365) : null)
			.RuleFor(a => a.LifetimeValue, (f, a) => a.TotalSpent * f.Random.Decimal(1.1m, 1.5m))
			.RuleFor(a => a.ChurnRiskScore, f => f.Random.Bool(0.7f) ? f.Random.Double(0, 1) : null)
			.RuleFor(a => a.EngagementScore, f => f.Random.Bool(0.7f) ? f.Random.Double(0, 100) : null);

		var customerFaker = new Faker<Customer>()
			.RuleFor(c => c.Id, f => f.Random.Guid().ToString("N"))
			.RuleFor(c => c.Email, f => f.Internet.Email())
			.RuleFor(c => c.FirstName, f => f.Name.FirstName())
			.RuleFor(c => c.LastName, f => f.Name.LastName())
			.RuleFor(c => c.Phone, f => f.Random.Bool(0.7f) ? f.Phone.PhoneNumber() : null)
			.RuleFor(c => c.CreatedAt, f => f.Date.Past(3))
			.RuleFor(c => c.LastLoginAt, (f, c) => f.Random.Bool(0.9f) ? f.Date.Between(c.CreatedAt, DateTime.UtcNow) : null)
			.RuleFor(c => c.LastOrderAt, (f, c) => f.Random.Bool(0.7f) ? f.Date.Between(c.CreatedAt, DateTime.UtcNow) : null)
			.RuleFor(c => c.Tier, f => f.PickRandom<CustomerTier>())
			.RuleFor(c => c.IsVerified, f => f.Random.Bool(0.85f))
			.RuleFor(c => c.IsSubscribedToNewsletter, f => f.Random.Bool(0.4f))
			.RuleFor(c => c.Addresses, f => addressFaker.Generate(f.Random.Int(1, 3)))
			.RuleFor(c => c.Preferences, f => f.Random.Bool(0.8f) ? preferencesFaker.Generate() : null)
			.RuleFor(c => c.Analytics, f => f.Random.Bool(0.9f) ? analyticsFaker.Generate() : null)
			.RuleFor(c => c.Tags, f => f.Make(f.Random.Int(0, 4), () => f.PickRandom("VIP", "New", "Returning", "At-Risk", "High-Value")))
			.RuleFor(c => c.NameSuggest, (_, c) => $"{c.FirstName} {c.LastName}");

		return customerFaker.Generate(count);
	}
}
