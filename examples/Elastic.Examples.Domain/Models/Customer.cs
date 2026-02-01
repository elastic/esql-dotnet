// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;

namespace Elastic.Examples.Domain.Models;

/// <summary>
/// Customer profile with addresses, preferences, and analytics.
/// </summary>
[Index(
	Name = "customers",
	WriteAlias = "customers-write",
	SearchPattern = "customers*",
	Shards = 2,
	Replicas = 1,
	Dynamic = false
)]
public partial class Customer
{
	[JsonPropertyName("customer_id")]
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[Keyword]
	public string Email { get; set; } = string.Empty;

	[JsonPropertyName("first_name")]
	[Text(Analyzer = "standard")]
	public string FirstName { get; set; } = string.Empty;

	[JsonPropertyName("last_name")]
	[Text(Analyzer = "standard")]
	public string LastName { get; set; } = string.Empty;

	[JsonPropertyName("full_name")]
	[Text(Analyzer = "standard")]
	public string FullName => $"{FirstName} {LastName}";

	[Keyword]
	public string? Phone { get; set; }

	[Date(Format = "strict_date_optional_time")]
	public DateTime CreatedAt { get; set; }

	[Date(Format = "strict_date_optional_time")]
	public DateTime? LastLoginAt { get; set; }

	[Date(Format = "strict_date_optional_time")]
	public DateTime? LastOrderAt { get; set; }

	[Keyword]
	public CustomerTier Tier { get; set; }

	[JsonPropertyName("is_verified")]
	public bool IsVerified { get; set; }

	[JsonPropertyName("is_subscribed")]
	public bool IsSubscribedToNewsletter { get; set; }

	[Nested]
	public List<Address> Addresses { get; set; } = [];

	[Object]
	public CustomerPreferences? Preferences { get; set; }

	[Object]
	public CustomerAnalytics? Analytics { get; set; }

	[Keyword]
	public List<string> Tags { get; set; } = [];

	[Completion(Analyzer = "simple")]
	public string? NameSuggest { get; set; }

	[JsonIgnore]
	public string HashedPassword { get; set; } = string.Empty;

	[JsonIgnore]
	public string? AuthToken { get; set; }
}

public enum CustomerTier
{
	Bronze,
	Silver,
	Gold,
	Platinum,
	Diamond
}

/// <summary>
/// Customer preferences and settings.
/// </summary>
public class CustomerPreferences
{
	[Keyword]
	public string? PreferredLanguage { get; set; }

	[Keyword]
	public string? PreferredCurrency { get; set; }

	[Keyword]
	public string? TimeZone { get; set; }

	[Keyword]
	public List<string> FavoriteCategories { get; set; } = [];

	[Keyword]
	public List<string> FavoriteBrands { get; set; } = [];

	public bool EmailNotifications { get; set; } = true;
	public bool SmsNotifications { get; set; }
	public bool PushNotifications { get; set; } = true;
}

/// <summary>
/// Customer analytics and metrics.
/// </summary>
public class CustomerAnalytics
{
	[JsonPropertyName("total_orders")]
	public int TotalOrders { get; set; }

	[JsonPropertyName("total_spent")]
	public decimal TotalSpent { get; set; }

	[JsonPropertyName("avg_order_value")]
	public decimal AverageOrderValue { get; set; }

	[JsonPropertyName("days_since_last_order")]
	public int? DaysSinceLastOrder { get; set; }

	[JsonPropertyName("lifetime_value")]
	public decimal LifetimeValue { get; set; }

	[JsonPropertyName("churn_risk_score")]
	public double? ChurnRiskScore { get; set; }

	[JsonPropertyName("engagement_score")]
	public double? EngagementScore { get; set; }
}
