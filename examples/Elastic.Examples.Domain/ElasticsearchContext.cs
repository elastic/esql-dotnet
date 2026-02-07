// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Examples.Domain.Models;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

namespace Elastic.Examples.Domain;

/// <summary>
/// Elasticsearch mapping context for all example domain types.
/// </summary>
[ElasticsearchMappingContext]
[Index<Product>(
	Name = "products",
	WriteAlias = "products-write",
	ReadAlias = "products-read",
	SearchPattern = "products*",
	RefreshInterval = "1s",
	Configuration = typeof(ProductConfiguration)
)]
[Index<Order>(
	WriteAlias = "orders-write",
	ReadAlias = "orders-read",
	SearchPattern = "orders-*",
	DatePattern = "yyyy.MM"
)]
[Index<Customer>(
	Name = "customers",
	WriteAlias = "customers-write",
	SearchPattern = "customers*",
	Dynamic = false
)]
[DataStream<ApplicationLog>(Type = "logs", Dataset = "ecommerce.app", Namespace = "production")]
[DataStream<ApplicationMetric>(Type = "metrics", Dataset = "ecommerce.app", Namespace = "production")]
public static partial class ExampleElasticsearchContext
{
	// =========================================================================
	// Order: context-level Configure methods
	// =========================================================================

	/// <summary>Configures Order-specific analysis settings.</summary>
	public static AnalysisBuilder ConfigureOrderAnalysis(AnalysisBuilder analysis) => analysis
		.TokenFilter("order_shingle", f => f
			.Shingle()
			.MinShingleSize(2)
			.MaxShingleSize(3)
			.OutputUnigrams(true))
		.Analyzer("order_notes_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding, OrderAnalysis.TokenFilters.OrderShingle));

	/// <summary>Configures Order-specific mapping overrides.</summary>
	public static OrderMappingsBuilder ConfigureOrderMappings(OrderMappingsBuilder mappings) => mappings
		.Notes(f => f
			.Analyzer(mappings.Analysis.Analyzers.OrderNotesAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(1024)))
		.Status(f => f)
		.AddRuntimeField("order_age_days", r => r
			.Long()
			.Script("""
				long diff = System.currentTimeMillis() - doc['@timestamp'].value.toInstant().toEpochMilli();
				emit(diff / (1000 * 60 * 60 * 24));
				"""))
		.AddRuntimeField("net_amount", r => r
			.Double()
			.Script("emit(doc['total_amount'].value - doc['discount_amount'].value)"))
		.AddRuntimeField("status_display", r => r
			.Keyword()
			.Script("""
				def status = doc['status'].value;
				def display = ['Pending': 'Awaiting Confirmation', 'Confirmed': 'Order Confirmed',
				               'Processing': 'Being Prepared', 'Shipped': 'In Transit',
				               'Delivered': 'Delivered', 'Cancelled': 'Cancelled', 'Refunded': 'Refunded'];
				emit(display.getOrDefault(status, status));
				"""));

	// =========================================================================
	// Customer: context-level Configure methods
	// =========================================================================

	/// <summary>Configures Customer-specific analysis settings.</summary>
	public static AnalysisBuilder ConfigureCustomerAnalysis(AnalysisBuilder analysis) => analysis
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
			.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding, CustomerAnalysis.TokenFilters.NameEdgeNgram))
		.Analyzer("name_search_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding))
		.Analyzer("email_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.UaxUrlEmail)
			.Filter(TokenFilters.Lowercase));

	/// <summary>Configures Customer-specific mapping overrides.</summary>
	public static CustomerMappingsBuilder ConfigureCustomerMappings(CustomerMappingsBuilder mappings) => mappings
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

	// =========================================================================
	// ApplicationLog: context-level Configure methods
	// =========================================================================

	/// <summary>Configures ApplicationLog-specific analysis settings.</summary>
	public static AnalysisBuilder ConfigureApplicationLogAnalysis(AnalysisBuilder analysis) => analysis
		.Tokenizer("log_tokenizer", t => t
			.Pattern()
			.PatternValue("[\\s\\[\\]{}(),;:=|/\\\\]+"))
		.TokenFilter("log_word_delimiter", f => f
			.WordDelimiterGraph()
			.PreserveOriginal(true)
			.SplitOnCaseChange(true)
			.SplitOnNumerics(true))
		.TokenFilter("exception_word_delimiter", f => f
			.WordDelimiterGraph()
			.PreserveOriginal(true)
			.SplitOnCaseChange(true))
		.Analyzer("log_message_analyzer", a => a
			.Custom()
			.Tokenizer(ApplicationLogAnalysis.Tokenizers.LogTokenizer)
			.Filters(TokenFilters.Lowercase, ApplicationLogAnalysis.TokenFilters.LogWordDelimiter))
		.Analyzer("error_message_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(TokenFilters.Lowercase, ApplicationLogAnalysis.TokenFilters.ExceptionWordDelimiter));

	/// <summary>Configures ApplicationLog-specific mapping overrides.</summary>
	public static ApplicationLogMappingsBuilder ConfigureApplicationLogMappings(ApplicationLogMappingsBuilder mappings) => mappings
		.Message(f => f
			.Analyzer(mappings.Analysis.Analyzers.LogMessageAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(2048)))
		.ErrorMessage(f => f
			.Analyzer(mappings.Analysis.Analyzers.ErrorMessageAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(1024)))
		.Level(f => f)
		.AddRuntimeField("is_error", r => r
			.Boolean()
			.Script("emit(doc['log.level'].value == 'Error' || doc['log.level'].value == 'Fatal')"))
		.AddRuntimeField("response_time_ms", r => r
			.Double()
			.Script("if (doc['event.duration'].size() > 0) emit(doc['event.duration'].value / 1000000.0)"))
		.AddRuntimeField("hour_of_day", r => r
			.Long()
			.Script("emit(doc['@timestamp'].value.getHour())"))
		.AddDynamicTemplate("labels_as_keyword", dt => dt
			.PathMatch("labels.*")
			.Mapping(m => m.Keyword()));
}
