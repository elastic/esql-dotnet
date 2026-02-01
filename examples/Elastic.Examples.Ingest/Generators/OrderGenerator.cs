// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;
using Bogus;
using Elastic.Examples.Domain.Models;

namespace Elastic.Examples.Ingest.Generators;

/// <summary>Generates fake Order data for testing and demos.</summary>
public static class OrderGenerator
{
	private const int Seed = 12345;

	private static readonly string[] ShippingMethods = ["Standard", "Express", "Overnight", "Economy", "Same-Day"];
	private static readonly string[] Carriers = ["FedEx", "UPS", "USPS", "DHL", "Amazon Logistics"];
	private static readonly string[] PaymentMethods = ["Credit Card", "Debit Card", "PayPal", "Apple Pay", "Google Pay", "Bank Transfer"];
	private static readonly string[] PromoCodes = ["SAVE10", "SUMMER20", "WELCOME15", "VIP25", "FLASH30", "FREESHIP"];

	public static IReadOnlyList<Order> Generate(
		IReadOnlyList<string> productIds,
		IReadOnlyList<string> customerIds,
		int count = 5000)
	{
		Randomizer.Seed = new Random(Seed);

		var addressFaker = new Faker<Address>()
			.RuleFor(a => a.Street, f => f.Address.StreetAddress())
			.RuleFor(a => a.City, f => f.Address.City())
			.RuleFor(a => a.State, f => f.Address.StateAbbr())
			.RuleFor(a => a.PostalCode, f => f.Address.ZipCode())
			.RuleFor(a => a.Country, f => f.Address.CountryCode());

		var shippingFaker = new Faker<ShippingInfo>()
			.RuleFor(s => s.Method, f => f.PickRandom(ShippingMethods))
			.RuleFor(s => s.Carrier, f => f.PickRandom(Carriers))
			.RuleFor(s => s.TrackingNumber, f => f.Random.Bool(0.7f) ? f.Random.AlphaNumeric(18).ToUpper(CultureInfo.InvariantCulture) : null)
			.RuleFor(s => s.Address, f => addressFaker.Generate())
			.RuleFor(s => s.EstimatedDelivery, f => f.Date.Future(14))
			.RuleFor(s => s.ActualDelivery, (f, s) => f.Random.Bool(0.5f) ? f.Date.Between(DateTime.UtcNow.AddDays(-30), s.EstimatedDelivery ?? DateTime.UtcNow.AddDays(7)) : null);

		var paymentFaker = new Faker<PaymentInfo>()
			.RuleFor(p => p.Method, f => f.PickRandom(PaymentMethods))
			.RuleFor(p => p.TransactionId, f => f.Random.Guid().ToString("N"))
			.RuleFor(p => p.Status, f => f.PickRandom("Completed", "Pending", "Failed", "Refunded"))
			.RuleFor(p => p.Last4Digits, f => f.Random.Bool(0.6f) ? f.Finance.CreditCardNumber()[^4..] : null)
			.RuleFor(p => p.ProcessedAt, f => f.Date.Recent(7));

		var geoFaker = new Faker<GeoLocation>()
			.RuleFor(g => g.Lat, f => f.Address.Latitude())
			.RuleFor(g => g.Lon, f => f.Address.Longitude());

		var orders = new List<Order>(count);
		var faker = new Faker();

		for (var i = 0; i < count; i++)
		{
			var itemCount = faker.Random.Int(1, 5);
			var items = new List<OrderLineItem>(itemCount);

			for (var j = 0; j < itemCount; j++)
			{
				var unitPrice = Math.Round((decimal)faker.Random.Double(9.99, 299.99), 2);
				var quantity = faker.Random.Int(1, 5);
				items.Add(new OrderLineItem
				{
					ProductId = productIds[faker.Random.Int(0, productIds.Count - 1)],
					Sku = faker.Commerce.Ean13(),
					Name = faker.Commerce.ProductName(),
					Quantity = quantity,
					UnitPrice = unitPrice,
					TotalPrice = unitPrice * quantity,
					DiscountPercent = faker.Random.Bool(0.2f) ? faker.Random.Double(5, 25) : null
				});
			}

			var promoCodes = faker.Random.Bool(0.3f) ? [faker.PickRandom(PromoCodes)] : new List<string>();
			var itemsTotal = items.Sum(item => item.TotalPrice);
			var discountAmount = promoCodes.Count > 0 ? Math.Round((decimal)faker.Random.Double(5, 50), 2) : 0;
			var taxAmount = Math.Round(itemsTotal * 0.08m, 2);
			var shippingAmount = Math.Round((decimal)faker.Random.Double(0, 25), 2);

			orders.Add(new Order
			{
				Id = faker.Random.Guid().ToString("N"),
				CustomerId = customerIds[faker.Random.Int(0, customerIds.Count - 1)],
				Timestamp = faker.Date.Past(1),
				Status = faker.PickRandom<OrderStatus>(),
				Currency = faker.PickRandom("USD", "EUR", "GBP", "CAD"),
				Items = items,
				Shipping = faker.Random.Bool(0.95f) ? shippingFaker.Generate() : null,
				Payment = faker.Random.Bool(0.98f) ? paymentFaker.Generate() : null,
				DeliveryLocation = faker.Random.Bool(0.4f) ? geoFaker.Generate() : null,
				CustomerIp = faker.Random.Bool(0.8f) ? faker.Internet.IpAddress().ToString() : null,
				Notes = faker.Random.Bool(0.2f) ? faker.Lorem.Sentence() : null,
				PromoCodes = promoCodes,
				DiscountAmount = discountAmount,
				TaxAmount = taxAmount,
				ShippingAmount = shippingAmount,
				TotalAmount = itemsTotal + taxAmount + shippingAmount - discountAmount
			});
		}

		return orders;
	}
}
