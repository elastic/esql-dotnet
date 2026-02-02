// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

namespace Elastic.Examples.Domain.Models;

/// <summary>
/// Customer profile with addresses, preferences, and analytics.
/// </summary>
[Index(
	Name = "customers",
	WriteAlias = "customers-write",
	SearchPattern = "customers*",
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
	[Text(Analyzer = "name_analyzer", SearchAnalyzer = "name_search_analyzer")]
	public string FirstName { get; set; } = string.Empty;

	[JsonPropertyName("last_name")]
	[Text(Analyzer = "name_analyzer", SearchAnalyzer = "name_search_analyzer")]
	public string LastName { get; set; } = string.Empty;

	[JsonPropertyName("full_name")]
	[Text(Analyzer = "name_analyzer", SearchAnalyzer = "name_search_analyzer")]
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

	/// <summary>Configures Customer-specific analysis settings.</summary>
	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
		.TokenFilter("name_edge_ngram", f => f
			.EdgeNGram()
			.MinGram(1)
			.MaxGram(25))
		.Normalizer("lowercase_normalizer", n => n
			.Custom()
			.Filter(TokenFilters.Lowercase))
		.Analyzer("name_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding, Analysis.TokenFilters.NameEdgeNgram))
		.Analyzer("name_search_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding))
		.Analyzer("email_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.UaxUrlEmail)
			.Filter(TokenFilters.Lowercase));

	/// <summary>Configures Customer-specific mapping overrides.</summary>
	public static CustomerMappingsBuilder ConfigureMappings(CustomerMappingsBuilder mappings) => mappings
		.Email(f => f
			.Normalizer(mappings.Analysis.Normalizers.LowercaseNormalizer)
			.MultiField("analyzed", mf => mf.Text().Analyzer(mappings.Analysis.Analyzers.EmailAnalyzer)))
		.Addresses(a => a
			.Street(f => f.Analyzer(mappings.Analysis.Analyzers.Standard))
			.City(f => f.Normalizer(mappings.Analysis.Normalizers.LowercaseNormalizer))
			.Country(f => f.Normalizer(mappings.Analysis.Normalizers.LowercaseNormalizer)))
		.FirstName(f => f
			.Analyzer(mappings.Analysis.Analyzers.NameAnalyzer)
			.SearchAnalyzer(mappings.Analysis.Analyzers.NameSearchAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(100)))
		.LastName(f => f
			.Analyzer(mappings.Analysis.Analyzers.NameAnalyzer)
			.SearchAnalyzer(mappings.Analysis.Analyzers.NameSearchAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(100)))
		.AddField("full_name_search", f => f
			.Text()
			.Analyzer(mappings.Analysis.Analyzers.NameAnalyzer)
			.SearchAnalyzer(mappings.Analysis.Analyzers.NameSearchAnalyzer))
		.AddRuntimeField("days_since_last_order", r => r
			.Long()
			.Script("""
				if (doc['lastOrderAt'].size() > 0) {
					long diff = System.currentTimeMillis() - doc['lastOrderAt'].value.toInstant().toEpochMilli();
					emit(diff / (1000 * 60 * 60 * 24));
				}
				"""))
		.AddRuntimeField("is_active", r => r
			.Boolean()
			.Script("""
				if (doc['lastOrderAt'].size() > 0) {
					long diff = System.currentTimeMillis() - doc['lastOrderAt'].value.toInstant().toEpochMilli();
					emit(diff < 90L * 24 * 60 * 60 * 1000);
				} else {
					emit(false);
				}
				"""));
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
