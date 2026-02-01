// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Examples.Domain.Models;

Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine("Elastic.Mapping Source Generator Examples");
Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine();

// ============================================================================
// PRODUCT INDEX - Traditional Index with Aliases
// ============================================================================
Console.WriteLine("1. PRODUCT INDEX (Traditional Index with Aliases)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy:");
Console.WriteLine($"   Write Target: {Product.ElasticsearchContext.IndexStrategy.WriteTarget}");
Console.WriteLine($"   Date Pattern: {Product.ElasticsearchContext.IndexStrategy.DatePattern ?? "N/A"}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {Product.ElasticsearchContext.SearchStrategy.Pattern}");
Console.WriteLine($"   Read Alias: {Product.ElasticsearchContext.SearchStrategy.ReadAlias}");

Console.WriteLine("\n>> Hashes (for change detection):");
Console.WriteLine($"   Combined Hash: {Product.ElasticsearchContext.Hash}");
Console.WriteLine($"   Settings Hash: {Product.ElasticsearchContext.SettingsHash}");
Console.WriteLine($"   Mappings Hash: {Product.ElasticsearchContext.MappingsHash}");

Console.WriteLine("\n>> Field Constants (for type-safe queries):");
Console.WriteLine($"   Product.ElasticsearchContext.Fields.Id = \"{Product.ElasticsearchContext.Fields.Id}\"");
Console.WriteLine($"   Product.ElasticsearchContext.Fields.Name = \"{Product.ElasticsearchContext.Fields.Name}\"");
Console.WriteLine($"   Product.ElasticsearchContext.Fields.Price = \"{Product.ElasticsearchContext.Fields.Price}\"");
Console.WriteLine($"   Product.ElasticsearchContext.Fields.Categories = \"{Product.ElasticsearchContext.Fields.Categories}\"");

Console.WriteLine("\n>> Settings JSON:");
Console.WriteLine(Product.ElasticsearchContext.GetSettingsJson());

Console.WriteLine("\n>> Mappings JSON (first 1000 chars):");
var productMappings = Product.ElasticsearchContext.GetMappingJson();
Console.WriteLine(productMappings.Length > 1000 ? productMappings[..1000] + "..." : productMappings);

// ============================================================================
// ORDER INDEX - Traditional Index with Rolling Date Pattern
// ============================================================================
Console.WriteLine("\n\n2. ORDER INDEX (Rolling Date Pattern)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy:");
Console.WriteLine($"   Write Target: {Order.ElasticsearchContext.IndexStrategy.WriteTarget}");
Console.WriteLine($"   Date Pattern: {Order.ElasticsearchContext.IndexStrategy.DatePattern}");
Console.WriteLine($"   Today's Index: {Order.ElasticsearchContext.IndexStrategy.GetWriteTarget(DateTime.UtcNow)}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {Order.ElasticsearchContext.SearchStrategy.Pattern}");
Console.WriteLine($"   Read Alias: {Order.ElasticsearchContext.SearchStrategy.ReadAlias}");

Console.WriteLine("\n>> Field Constants:");
Console.WriteLine($"   Order.ElasticsearchContext.Fields.Id = \"{Order.ElasticsearchContext.Fields.Id}\"");
Console.WriteLine($"   Order.ElasticsearchContext.Fields.Timestamp = \"{Order.ElasticsearchContext.Fields.Timestamp}\"");
Console.WriteLine($"   Order.ElasticsearchContext.Fields.TotalAmount = \"{Order.ElasticsearchContext.Fields.TotalAmount}\"");
Console.WriteLine($"   Order.ElasticsearchContext.Fields.Items = \"{Order.ElasticsearchContext.Fields.Items}\"");

// ============================================================================
// APPLICATION LOG - Data Stream
// ============================================================================
Console.WriteLine("\n\n3. APPLICATION LOG (Data Stream)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy (Data Stream):");
Console.WriteLine($"   Data Stream Name: {ApplicationLog.ElasticsearchContext.IndexStrategy.DataStreamName}");
Console.WriteLine($"   Type: {ApplicationLog.ElasticsearchContext.IndexStrategy.Type}");
Console.WriteLine($"   Dataset: {ApplicationLog.ElasticsearchContext.IndexStrategy.Dataset}");
Console.WriteLine($"   Namespace: {ApplicationLog.ElasticsearchContext.IndexStrategy.Namespace}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {ApplicationLog.ElasticsearchContext.SearchStrategy.Pattern}");

Console.WriteLine("\n>> Field Constants (ECS-style fields):");
Console.WriteLine($"   Timestamp = \"{ApplicationLog.ElasticsearchContext.Fields.Timestamp}\"");
Console.WriteLine($"   Level = \"{ApplicationLog.ElasticsearchContext.Fields.Level}\"");
Console.WriteLine($"   Logger = \"{ApplicationLog.ElasticsearchContext.Fields.Logger}\"");
Console.WriteLine($"   ServiceName = \"{ApplicationLog.ElasticsearchContext.Fields.ServiceName}\"");
Console.WriteLine($"   TraceId = \"{ApplicationLog.ElasticsearchContext.Fields.TraceId}\"");

// ============================================================================
// APPLICATION METRIC - Data Stream
// ============================================================================
Console.WriteLine("\n\n4. APPLICATION METRIC (Data Stream)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy (Data Stream):");
Console.WriteLine($"   Data Stream Name: {ApplicationMetric.ElasticsearchContext.IndexStrategy.DataStreamName}");
Console.WriteLine($"   Type: {ApplicationMetric.ElasticsearchContext.IndexStrategy.Type}");
Console.WriteLine($"   Dataset: {ApplicationMetric.ElasticsearchContext.IndexStrategy.Dataset}");
Console.WriteLine($"   Namespace: {ApplicationMetric.ElasticsearchContext.IndexStrategy.Namespace}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {ApplicationMetric.ElasticsearchContext.SearchStrategy.Pattern}");

// ============================================================================
// CUSTOMER INDEX - Dynamic Disabled
// ============================================================================
Console.WriteLine("\n\n5. CUSTOMER INDEX (Dynamic Mapping Disabled)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy:");
Console.WriteLine($"   Write Target: {Customer.ElasticsearchContext.IndexStrategy.WriteTarget}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {Customer.ElasticsearchContext.SearchStrategy.Pattern}");

Console.WriteLine("\n>> Mappings JSON (first 500 chars):");
var customerMappings = Customer.ElasticsearchContext.GetMappingJson();
Console.WriteLine(customerMappings.Length > 500 ? customerMappings[..500] + "..." : customerMappings);

// ============================================================================
// RUNTIME OVERRIDES with Fluent API
// ============================================================================
Console.WriteLine("\n\n6. RUNTIME OVERRIDES (Fluent API)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Original Product Hash: " + Product.ElasticsearchContext.Hash);

// Override some settings at runtime
var customProductConfig = Product.MappingConfig()
	.Name(f => f.Analyzer("english_stemmed"))
	.Description(f => f.Analyzer("english_stemmed").Norms(true))
	.Sku(f => f.IgnoreAbove(128));

Console.WriteLine(">> Custom Config Hash: " + customProductConfig.ComputeHash());
Console.WriteLine("   (Different because we changed analyzers)");

Console.WriteLine("\n>> Custom Mappings JSON (first 800 chars):");
var customMappings = customProductConfig.GetMappingJson();
Console.WriteLine(customMappings.Length > 800 ? customMappings[..800] + "..." : customMappings);

// Another override example for logs
Console.WriteLine("\n>> Application Log with custom settings:");
var customLogConfig = ApplicationLog.MappingConfig()
	.Message(f => f.Analyzer("keyword"))
	.StackTrace(f => f.Index(false));

Console.WriteLine("   Custom Log Hash: " + customLogConfig.ComputeHash());

// ============================================================================
// COMPLETE INDEX JSON (Settings + Mappings)
// ============================================================================
Console.WriteLine("\n\n7. COMPLETE INDEX JSON (for Index Creation)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Product Complete Index JSON:");
var productIndex = Product.ElasticsearchContext.GetIndexJson();
Console.WriteLine(productIndex.Length > 1200 ? productIndex[..1200] + "..." : productIndex);

// ============================================================================
// PRACTICAL USAGE PATTERNS
// ============================================================================
Console.WriteLine("\n\n8. PRACTICAL USAGE PATTERNS");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Pattern 1: Check if mapping changed before updating");
Console.WriteLine("   var currentHash = GetHashFromCluster();");
Console.WriteLine($"   if (currentHash != Product.ElasticsearchContext.Hash) UpdateMapping();");

Console.WriteLine("\n>> Pattern 2: Create index with settings");
Console.WriteLine("   await client.Indices.CreateAsync(");
Console.WriteLine($"       \"{Product.ElasticsearchContext.IndexStrategy.WriteTarget}\",");
Console.WriteLine("       c => c.InitializeUsing(Product.ElasticsearchContext.GetIndexJson()));");

Console.WriteLine("\n>> Pattern 3: Query with type-safe field names");
Console.WriteLine("   var esql = $\"FROM {Product.ElasticsearchContext.SearchStrategy.Pattern}\";");
Console.WriteLine($"   esql += $\" | WHERE {Product.ElasticsearchContext.Fields.InStock} == true\";");
Console.WriteLine($"   esql += $\" | WHERE {Product.ElasticsearchContext.Fields.Price} < 100\";");

Console.WriteLine("\n>> Pattern 4: Rolling index for orders");
var todayIndex = Order.ElasticsearchContext.IndexStrategy.GetWriteTarget(DateTime.UtcNow);
var lastMonthIndex = Order.ElasticsearchContext.IndexStrategy.GetWriteTarget(DateTime.UtcNow.AddMonths(-1));
Console.WriteLine($"   Today's index: {todayIndex}");
Console.WriteLine($"   Last month's index: {lastMonthIndex}");

Console.WriteLine("\n>> Pattern 5: Data stream for logs");
Console.WriteLine($"   var dataStream = \"{ApplicationLog.ElasticsearchContext.IndexStrategy.DataStreamName}\";");
Console.WriteLine("   // Logs automatically go to correct backing index");

Console.WriteLine("\n\n" + "=".PadRight(80, '='));
Console.WriteLine("All mappings are generated at compile-time - no reflection at runtime!");
Console.WriteLine("=".PadRight(80, '='));
