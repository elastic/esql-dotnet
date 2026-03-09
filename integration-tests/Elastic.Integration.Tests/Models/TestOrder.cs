// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Integration.Tests.Models;

public class TestOrder
{
	[JsonPropertyName("order_id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("customer_id")]
	public string CustomerId { get; set; } = string.Empty;

	[JsonPropertyName("@timestamp")]
	public DateTime Timestamp { get; set; }

	public OrderStatus Status { get; set; }

	[JsonPropertyName("total_amount")]
	public decimal TotalAmount { get; set; }

	public string Currency { get; set; } = "USD";

	[JsonPropertyName("client_ip")]
	public string? ClientIp { get; set; }

	[JsonPropertyName("discount_pct")]
	public double? DiscountPercent { get; set; }

	[JsonPropertyName("promo_codes")]
	public List<string> PromoCodes { get; set; } = [];

	public string? Notes { get; set; }
}

public enum OrderStatus
{
	Pending,
	Confirmed,
	Shipped,
	Delivered,
	Cancelled
}
