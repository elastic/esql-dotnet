// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

namespace Elastic.Examples.Domain.Models;

/// <summary>
/// Configuration class for Product analysis and mappings.
/// Referenced via <c>Configuration = typeof(ProductConfiguration)</c> in the Index attribute.
/// </summary>
public static class ProductConfiguration
{
	/// <summary>Configures Product-specific analysis settings.</summary>
	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
		.TokenFilter("product_edge_ngram", f => f
			.EdgeNGram()
			.MinGram(2)
			.MaxGram(20))
		.TokenFilter("english_stop", f => f
			.Stop()
			.Stopwords(StopWords.English))
		.TokenFilter("english_stemmer", f => f
			.Stemmer()
			.Language(StemmerLanguages.English))
		.Normalizer("sku_normalizer", n => n
			.Custom()
			.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding))
		.Analyzer("product_name_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(TokenFilters.Lowercase, ProductAnalysis.TokenFilters.ProductEdgeNgram, TokenFilters.AsciiFolding))
		.Analyzer("product_name_search_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding))
		.Analyzer("product_description_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(
				TokenFilters.Lowercase,
				ProductAnalysis.TokenFilters.EnglishStop,
				ProductAnalysis.TokenFilters.EnglishStemmer,
				TokenFilters.AsciiFolding
			)
		);

	/// <summary>Configures Product-specific mapping overrides.</summary>
	public static ProductMappingsBuilder ConfigureMappings(ProductMappingsBuilder mappings) => mappings
		.Name(f => f
			.Analyzer(mappings.Analysis.Analyzers.ProductNameAnalyzer)
			.SearchAnalyzer(mappings.Analysis.Analyzers.ProductNameSearchAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256))
			.MultiField("autocomplete", mf => mf.SearchAsYouType()))
		.Description(f => f
			.Analyzer(mappings.Analysis.Analyzers.ProductDescriptionAnalyzer)
			.Norms(false)
			.MultiField("raw", mf => mf.Keyword().IgnoreAbove(512)))
		.Sku(f => f
			.Normalizer(mappings.Analysis.Normalizers.SkuNormalizer))
		.AddRuntimeField("discount_pct", r => r
			.Double()
			.Script("""
				if (doc['sale_price_usd'].size() > 0 && doc['price_usd'].size() > 0) {
					double sale = doc['sale_price_usd'].value;
					double price = doc['price_usd'].value;
					if (price > 0) emit((price - sale) / price * 100);
				}
				"""))
		.AddRuntimeField("is_on_sale", r => r
			.Boolean()
			.Script("emit(doc['sale_price_usd'].size() > 0 && doc['sale_price_usd'].value < doc['price_usd'].value)"));
}
