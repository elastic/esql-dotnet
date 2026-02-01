// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;

namespace Elastic.Examples.Domain.Models;

/// <summary>
/// E-commerce order with line items, shipping, and payment info.
/// Demonstrates traditional index with rolling date pattern.
/// </summary>
[Index(
	WriteAlias = "orders-write",
	ReadAlias = "orders-read",
	SearchPattern = "orders-*",
	DatePattern = "yyyy.MM",
	Shards = 2,
	Replicas = 1
)]
public partial class Order
{
	[JsonPropertyName("order_id")]
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("customer_id")]
	[Keyword]
	public string CustomerId { get; set; } = string.Empty;

	[JsonPropertyName("@timestamp")]
	[Date(Format = "strict_date_optional_time")]
	public DateTime Timestamp { get; set; }

	[Keyword]
	public OrderStatus Status { get; set; }

	[JsonPropertyName("total_amount")]
	public decimal TotalAmount { get; set; }

	[Keyword]
	public string Currency { get; set; } = "USD";

	[Nested]
	public List<OrderLineItem> Items { get; set; } = [];

	[Object]
	public ShippingInfo? Shipping { get; set; }

	[Object]
	public PaymentInfo? Payment { get; set; }

	[GeoPoint]
	public GeoLocation? DeliveryLocation { get; set; }

	[Ip]
	public string? CustomerIp { get; set; }

	[Text(Analyzer = "standard")]
	public string? Notes { get; set; }

	[Keyword]
	public List<string> PromoCodes { get; set; } = [];

	[JsonPropertyName("discount_amount")]
	public decimal DiscountAmount { get; set; }

	[JsonPropertyName("tax_amount")]
	public decimal TaxAmount { get; set; }

	[JsonPropertyName("shipping_amount")]
	public decimal ShippingAmount { get; set; }

	[JsonIgnore]
	public string ProcessingNotes { get; set; } = string.Empty;
}

public enum OrderStatus
{
	Pending,
	Confirmed,
	Processing,
	Shipped,
	Delivered,
	Cancelled,
	Refunded
}

/// <summary>
/// Individual line item in an order.
/// </summary>
public class OrderLineItem
{
	[JsonPropertyName("product_id")]
	[Keyword]
	public string ProductId { get; set; } = string.Empty;

	[Keyword]
	public string Sku { get; set; } = string.Empty;

	[Keyword]
	public string Name { get; set; } = string.Empty;

	public int Quantity { get; set; }

	[JsonPropertyName("unit_price")]
	public decimal UnitPrice { get; set; }

	[JsonPropertyName("total_price")]
	public decimal TotalPrice { get; set; }

	[JsonPropertyName("discount_percent")]
	public double? DiscountPercent { get; set; }
}

/// <summary>
/// Shipping information for an order.
/// </summary>
public class ShippingInfo
{
	[Keyword]
	public string Method { get; set; } = string.Empty;

	[Keyword]
	public string Carrier { get; set; } = string.Empty;

	[Keyword]
	public string? TrackingNumber { get; set; }

	[Object]
	public Address? Address { get; set; }

	[Date]
	public DateTime? EstimatedDelivery { get; set; }

	[Date]
	public DateTime? ActualDelivery { get; set; }
}

/// <summary>
/// Payment information for an order.
/// </summary>
public class PaymentInfo
{
	[Keyword]
	public string Method { get; set; } = string.Empty;

	[Keyword]
	public string? TransactionId { get; set; }

	[Keyword]
	public string Status { get; set; } = string.Empty;

	[Keyword]
	public string? Last4Digits { get; set; }

	[Date]
	public DateTime? ProcessedAt { get; set; }
}

/// <summary>
/// Physical address.
/// </summary>
public class Address
{
	[Text]
	public string Street { get; set; } = string.Empty;

	[Keyword]
	public string City { get; set; } = string.Empty;

	[Keyword]
	public string State { get; set; } = string.Empty;

	[Keyword]
	public string PostalCode { get; set; } = string.Empty;

	[Keyword]
	public string Country { get; set; } = string.Empty;
}

/// <summary>
/// Geographic location.
/// </summary>
public class GeoLocation
{
	public double Lat { get; set; }
	public double Lon { get; set; }
}
