// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Examples.Domain;
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
Console.WriteLine($"   Write Target: {ExampleElasticsearchContext.Product.IndexStrategy.WriteTarget}");
Console.WriteLine($"   Date Pattern: {ExampleElasticsearchContext.Product.IndexStrategy.DatePattern ?? "N/A"}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {ExampleElasticsearchContext.Product.SearchStrategy.Pattern}");
Console.WriteLine($"   Read Alias: {ExampleElasticsearchContext.Product.SearchStrategy.ReadAlias}");

Console.WriteLine("\n>> Hashes (for change detection):");
Console.WriteLine($"   Combined Hash: {ExampleElasticsearchContext.Product.Hash}");
Console.WriteLine($"   Settings Hash: {ExampleElasticsearchContext.Product.SettingsHash}");
Console.WriteLine($"   Mappings Hash: {ExampleElasticsearchContext.Product.MappingsHash}");

Console.WriteLine("\n>> Field Constants (for type-safe queries):");
Console.WriteLine($"   Product.Fields.Id = \"{ExampleElasticsearchContext.Product.Fields.Id}\"");
Console.WriteLine($"   Product.Fields.Name = \"{ExampleElasticsearchContext.Product.Fields.Name}\"");
Console.WriteLine($"   Product.Fields.Price = \"{ExampleElasticsearchContext.Product.Fields.Price}\"");
Console.WriteLine($"   Product.Fields.Categories = \"{ExampleElasticsearchContext.Product.Fields.Categories}\"");

Console.WriteLine("\n>> Settings JSON:");
Console.WriteLine(ExampleElasticsearchContext.Product.GetSettingsJson());

Console.WriteLine("\n>> Mappings JSON (first 1000 chars):");
var productMappings = ExampleElasticsearchContext.Product.GetMappingJson();
Console.WriteLine(productMappings.Length > 1000 ? productMappings[..1000] + "..." : productMappings);

// ============================================================================
// ORDER INDEX - Traditional Index with Rolling Date Pattern
// ============================================================================
Console.WriteLine("\n\n2. ORDER INDEX (Rolling Date Pattern)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy:");
Console.WriteLine($"   Write Target: {ExampleElasticsearchContext.Order.IndexStrategy.WriteTarget}");
Console.WriteLine($"   Date Pattern: {ExampleElasticsearchContext.Order.IndexStrategy.DatePattern}");
Console.WriteLine($"   Today's Index: {ExampleElasticsearchContext.Order.IndexStrategy.GetWriteTarget(DateTime.UtcNow)}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {ExampleElasticsearchContext.Order.SearchStrategy.Pattern}");
Console.WriteLine($"   Read Alias: {ExampleElasticsearchContext.Order.SearchStrategy.ReadAlias}");

Console.WriteLine("\n>> Field Constants:");
Console.WriteLine($"   Order.Fields.Id = \"{ExampleElasticsearchContext.Order.Fields.Id}\"");
Console.WriteLine($"   Order.Fields.Timestamp = \"{ExampleElasticsearchContext.Order.Fields.Timestamp}\"");
Console.WriteLine($"   Order.Fields.TotalAmount = \"{ExampleElasticsearchContext.Order.Fields.TotalAmount}\"");
Console.WriteLine($"   Order.Fields.Items = \"{ExampleElasticsearchContext.Order.Fields.Items}\"");

// ============================================================================
// APPLICATION LOG - Data Stream
// ============================================================================
Console.WriteLine("\n\n3. APPLICATION LOG (Data Stream)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy (Data Stream):");
Console.WriteLine($"   Data Stream Name: {ExampleElasticsearchContext.ApplicationLog.IndexStrategy.DataStreamName}");
Console.WriteLine($"   Type: {ExampleElasticsearchContext.ApplicationLog.IndexStrategy.Type}");
Console.WriteLine($"   Dataset: {ExampleElasticsearchContext.ApplicationLog.IndexStrategy.Dataset}");
Console.WriteLine($"   Namespace: {ExampleElasticsearchContext.ApplicationLog.IndexStrategy.Namespace}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {ExampleElasticsearchContext.ApplicationLog.SearchStrategy.Pattern}");

Console.WriteLine("\n>> Field Constants (ECS-style fields):");
Console.WriteLine($"   Timestamp = \"{ExampleElasticsearchContext.ApplicationLog.Fields.Timestamp}\"");
Console.WriteLine($"   Level = \"{ExampleElasticsearchContext.ApplicationLog.Fields.Level}\"");
Console.WriteLine($"   Logger = \"{ExampleElasticsearchContext.ApplicationLog.Fields.Logger}\"");
Console.WriteLine($"   ServiceName = \"{ExampleElasticsearchContext.ApplicationLog.Fields.ServiceName}\"");
Console.WriteLine($"   TraceId = \"{ExampleElasticsearchContext.ApplicationLog.Fields.TraceId}\"");

// ============================================================================
// APPLICATION METRIC - Data Stream
// ============================================================================
Console.WriteLine("\n\n4. APPLICATION METRIC (Data Stream)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy (Data Stream):");
Console.WriteLine($"   Data Stream Name: {ExampleElasticsearchContext.ApplicationMetric.IndexStrategy.DataStreamName}");
Console.WriteLine($"   Type: {ExampleElasticsearchContext.ApplicationMetric.IndexStrategy.Type}");
Console.WriteLine($"   Dataset: {ExampleElasticsearchContext.ApplicationMetric.IndexStrategy.Dataset}");
Console.WriteLine($"   Namespace: {ExampleElasticsearchContext.ApplicationMetric.IndexStrategy.Namespace}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {ExampleElasticsearchContext.ApplicationMetric.SearchStrategy.Pattern}");

// ============================================================================
// CUSTOMER INDEX - Dynamic Disabled
// ============================================================================
Console.WriteLine("\n\n5. CUSTOMER INDEX (Dynamic Mapping Disabled)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Index Strategy:");
Console.WriteLine($"   Write Target: {ExampleElasticsearchContext.Customer.IndexStrategy.WriteTarget}");

Console.WriteLine("\n>> Search Strategy:");
Console.WriteLine($"   Pattern: {ExampleElasticsearchContext.Customer.SearchStrategy.Pattern}");

Console.WriteLine("\n>> Mappings JSON (first 500 chars):");
var customerMappings = ExampleElasticsearchContext.Customer.GetMappingJson();
Console.WriteLine(customerMappings.Length > 500 ? customerMappings[..500] + "..." : customerMappings);

// ============================================================================
// COMPLETE INDEX JSON (Settings + Mappings)
// ============================================================================
Console.WriteLine("\n\n6. COMPLETE INDEX JSON (for Index Creation)");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Product Complete Index JSON:");
var productIndex = ExampleElasticsearchContext.Product.GetIndexJson();
Console.WriteLine(productIndex.Length > 1200 ? productIndex[..1200] + "..." : productIndex);

// ============================================================================
// PRACTICAL USAGE PATTERNS
// ============================================================================
Console.WriteLine("\n\n7. PRACTICAL USAGE PATTERNS");
Console.WriteLine("-".PadRight(60, '-'));

Console.WriteLine("\n>> Pattern 1: Check if mapping changed before updating");
Console.WriteLine("   var currentHash = GetHashFromCluster();");
Console.WriteLine($"   if (currentHash != ExampleElasticsearchContext.Product.Hash) UpdateMapping();");

Console.WriteLine("\n>> Pattern 2: Create index with settings");
Console.WriteLine("   await client.Indices.CreateAsync(");
Console.WriteLine($"       \"{ExampleElasticsearchContext.Product.IndexStrategy.WriteTarget}\",");
Console.WriteLine("       c => c.InitializeUsing(ExampleElasticsearchContext.Product.GetIndexJson()));");

Console.WriteLine("\n>> Pattern 3: Query with type-safe field names");
Console.WriteLine($"   var esql = $\"FROM {{ExampleElasticsearchContext.Product.SearchStrategy.Pattern}}\";");
Console.WriteLine($"   esql += $\" | WHERE {{ExampleElasticsearchContext.Product.Fields.InStock}} == true\";");
Console.WriteLine($"   esql += $\" | WHERE {{ExampleElasticsearchContext.Product.Fields.Price}} < 100\";");

Console.WriteLine("\n>> Pattern 4: Rolling index for orders");
var todayIndex = ExampleElasticsearchContext.Order.IndexStrategy.GetWriteTarget(DateTime.UtcNow);
var lastMonthIndex = ExampleElasticsearchContext.Order.IndexStrategy.GetWriteTarget(DateTime.UtcNow.AddMonths(-1));
Console.WriteLine($"   Today's index: {todayIndex}");
Console.WriteLine($"   Last month's index: {lastMonthIndex}");

Console.WriteLine("\n>> Pattern 5: Data stream for logs");
Console.WriteLine($"   var dataStream = \"{ExampleElasticsearchContext.ApplicationLog.IndexStrategy.DataStreamName}\";");
Console.WriteLine("   // Logs automatically go to correct backing index");

Console.WriteLine("\n>> Pattern 6: All registered contexts");
Console.WriteLine($"   ExampleElasticsearchContext.All has {ExampleElasticsearchContext.All.Count} registered types");

Console.WriteLine("\n\n" + "=".PadRight(80, '='));
Console.WriteLine("All mappings are generated at compile-time - no reflection at runtime!");
Console.WriteLine("=".PadRight(80, '='));
