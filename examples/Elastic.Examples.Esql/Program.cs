// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql;
using Elastic.Esql.Extensions;
using Elastic.Esql.Functions;
using Elastic.Examples.Domain.Models;

Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine("Elastic.Esql LINQ-to-ES|QL Examples");
Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine();
Console.WriteLine("Note: These examples show the ES|QL generated from LINQ queries.");
Console.WriteLine("In production, you would execute these against an Elasticsearch cluster.");
Console.WriteLine();

// ============================================================================
// PRODUCT QUERIES
// ============================================================================
Console.WriteLine("1. PRODUCT QUERIES");
Console.WriteLine("-".PadRight(60, '-'));

// Basic filter
Console.WriteLine("\n>> Find in-stock products under $50:");
var inStockProducts = Esql.From<Product>()
	.Where(p => p.InStock && p.Price < 50)
	.Select(p => new { p.Name, p.Price, p.Brand });

Console.WriteLine($"   {inStockProducts}");

// Full-text search (simulated with LIKE for now)
Console.WriteLine("\n>> Search products by name:");
var searchProducts = Esql.From<Product>()
	.Where(p => p.Name.Contains("laptop"))
	.OrderByDescending(p => p.AverageRating)
	.Take(20);

Console.WriteLine($"   {searchProducts}");

// Aggregations - top brands by average price
Console.WriteLine("\n>> Top 10 brands by average product price:");
var brandPrices = Esql.From<Product>()
	.GroupBy(p => p.Brand)
	.Select(g => new
	{
		Brand = g.Key,
		AvgPrice = g.Average(p => p.Price),
		ProductCount = g.Count()
	})
	.OrderByDescending(x => x.AvgPrice)
	.Take(10);

Console.WriteLine($"   {brandPrices}");

// Complex filter with date range
Console.WriteLine("\n>> Products created in the last 30 days with good ratings:");
var recentProducts = Esql.From<Product>()
	.Where(p => p.CreatedAt > DateTime.UtcNow.AddDays(-30))
	.Where(p => p.AverageRating >= 4.0)
	.OrderByDescending(p => p.CreatedAt)
	.Select(p => new { p.Name, p.CreatedAt, p.AverageRating });

Console.WriteLine($"   {recentProducts}");

// Stats on products
Console.WriteLine("\n>> Product statistics by brand:");
var productStats = Esql.From<Product>()
	.Where(p => p.InStock)
	.GroupBy(p => p.Brand)
	.Select(g => new
	{
		Brand = g.Key,
		TotalProducts = g.Count(),
		AvgPrice = g.Average(p => p.Price),
		MinPrice = g.Min(p => p.Price),
		MaxPrice = g.Max(p => p.Price)
	});

Console.WriteLine($"   {productStats}");

// ============================================================================
// ORDER QUERIES
// ============================================================================
Console.WriteLine("\n\n2. ORDER QUERIES");
Console.WriteLine("-".PadRight(60, '-'));

// Orders by status
Console.WriteLine("\n>> Pending orders with high value:");
var pendingOrders = Esql.From<Order>()
	.Where(o => o.Status == OrderStatus.Pending)
	.Where(o => o.TotalAmount > 500)
	.OrderByDescending(o => o.TotalAmount)
	.Take(50);

Console.WriteLine($"   {pendingOrders}");

// Daily order totals
Console.WriteLine("\n>> Daily order totals for the last week:");
var dailyTotals = Esql.From<Order>()
	.Where(o => o.Timestamp > DateTime.UtcNow.AddDays(-7))
	.GroupBy(o => o.Timestamp.Date)
	.Select(g => new
	{
		Date = g.Key,
		OrderCount = g.Count(),
		TotalRevenue = g.Sum(o => o.TotalAmount)
	})
	.OrderBy(x => x.Date);

Console.WriteLine($"   {dailyTotals}");

// Orders by customer
Console.WriteLine("\n>> Top customers by order value:");
var topCustomers = Esql.From<Order>()
	.Where(o => o.Status == OrderStatus.Delivered)
	.GroupBy(o => o.CustomerId)
	.Select(g => new
	{
		CustomerId = g.Key,
		OrderCount = g.Count(),
		TotalSpent = g.Sum(o => o.TotalAmount)
	})
	.OrderByDescending(x => x.TotalSpent)
	.Take(20);

Console.WriteLine($"   {topCustomers}");

// Shipping analysis
Console.WriteLine("\n>> Average delivery time by carrier:");
var carrierStats = Esql.From<Order>()
	.Where(o => o.Status == OrderStatus.Delivered)
	.GroupBy(o => o.Shipping!.Carrier)
	.Select(g => new
	{
		Carrier = g.Key,
		OrderCount = g.Count()
	});

Console.WriteLine($"   {carrierStats}");

// ============================================================================
// APPLICATION LOG QUERIES
// ============================================================================
Console.WriteLine("\n\n3. APPLICATION LOG QUERIES (Data Stream)");
Console.WriteLine("-".PadRight(60, '-'));

// Error logs
Console.WriteLine("\n>> Recent error logs:");
var errorLogs = Esql.From<ApplicationLog>()
	.Where(l => l.Level == LogLevel.Error || l.Level == LogLevel.Fatal)
	.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-1))
	.OrderByDescending(l => l.Timestamp)
	.Take(100);

Console.WriteLine($"   {errorLogs}");

// Errors by service
Console.WriteLine("\n>> Error count by service (last 24 hours):");
var errorsByService = Esql.From<ApplicationLog>()
	.Where(l => l.Level >= LogLevel.Error)
	.Where(l => l.Timestamp > DateTime.UtcNow.AddDays(-1))
	.GroupBy(l => l.ServiceName)
	.Select(g => new
	{
		Service = g.Key,
		ErrorCount = g.Count()
	})
	.OrderByDescending(x => x.ErrorCount);

Console.WriteLine($"   {errorsByService}");

// Trace analysis
Console.WriteLine("\n>> Logs for a specific trace:");
var traceLogs = Esql.From<ApplicationLog>()
	.Where(l => l.TraceId == "abc123xyz")
	.OrderBy(l => l.Timestamp)
	.Select(l => new { l.Timestamp, l.Level, l.ServiceName, l.Message });

Console.WriteLine($"   {traceLogs}");

// HTTP error analysis
Console.WriteLine("\n>> HTTP 5xx errors by endpoint:");
var httpErrors = Esql.From<ApplicationLog>()
	.Where(l => l.HttpStatusCode >= 500)
	.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-6))
	.GroupBy(l => l.UrlPath)
	.Select(g => new
	{
		Endpoint = g.Key,
		ErrorCount = g.Count()
	})
	.OrderByDescending(x => x.ErrorCount)
	.Take(20);

Console.WriteLine($"   {httpErrors}");

// ============================================================================
// APPLICATION METRIC QUERIES
// ============================================================================
Console.WriteLine("\n\n4. APPLICATION METRIC QUERIES (Data Stream)");
Console.WriteLine("-".PadRight(60, '-'));

// Current resource usage
Console.WriteLine("\n>> Latest metrics by host:");
var latestMetrics = Esql.From<ApplicationMetric>()
	.Where(m => m.Timestamp > DateTime.UtcNow.AddMinutes(-5))
	.OrderByDescending(m => m.Timestamp)
	.Select(m => new { m.HostName, m.CpuPercent, m.MemoryPercent })
	.Take(10);

Console.WriteLine($"   {latestMetrics}");

// Average latency over time
Console.WriteLine("\n>> Hourly average latency:");
var hourlyLatency = Esql.From<ApplicationMetric>()
	.Where(m => m.Timestamp > DateTime.UtcNow.AddDays(-1))
	.GroupBy(m => m.Timestamp.Hour)
	.Select(g => new
	{
		Hour = g.Key,
		AvgLatency = g.Average(m => m.LatencyAvgMs),
		P95Latency = g.Average(m => m.LatencyP95Ms)
	})
	.OrderBy(x => x.Hour);

Console.WriteLine($"   {hourlyLatency}");

// Business metrics
Console.WriteLine("\n>> Business metrics summary:");
var businessMetrics = Esql.From<ApplicationMetric>()
	.Where(m => m.Timestamp > DateTime.UtcNow.AddHours(-1))
	.Select(m => new
	{
		m.Timestamp,
		m.OrdersCount,
		m.OrdersValue,
		m.ActiveUsers,
		m.CartAbandonments
	})
	.OrderByDescending(m => m.Timestamp)
	.Take(60);

Console.WriteLine($"   {businessMetrics}");

// ============================================================================
// CUSTOMER QUERIES
// ============================================================================
Console.WriteLine("\n\n5. CUSTOMER QUERIES");
Console.WriteLine("-".PadRight(60, '-'));

// High-value customers
Console.WriteLine("\n>> High-value customers at churn risk:");
var churnRisk = Esql.From<Customer>()
	.Where(c => c.Tier == CustomerTier.Gold || c.Tier == CustomerTier.Platinum)
	.OrderByDescending(c => c.Analytics!.TotalSpent)
	.Select(c => new { c.Email, c.Tier, c.Analytics!.TotalSpent })
	.Take(100);

Console.WriteLine($"   {churnRisk}");

// Customer segments
Console.WriteLine("\n>> Customer count by tier:");
var tierCounts = Esql.From<Customer>()
	.GroupBy(c => c.Tier)
	.Select(g => new
	{
		Tier = g.Key,
		CustomerCount = g.Count()
	})
	.OrderByDescending(x => x.CustomerCount);

Console.WriteLine($"   {tierCounts}");

// Recently active customers
Console.WriteLine("\n>> Recently active verified customers:");
var activeCustomers = Esql.From<Customer>()
	.Where(c => c.IsVerified)
	.Where(c => c.LastLoginAt > DateTime.UtcNow.AddDays(-7))
	.OrderByDescending(c => c.LastLoginAt)
	.Take(50);

Console.WriteLine($"   {activeCustomers}");

// ============================================================================
// ADVANCED PATTERNS
// ============================================================================
Console.WriteLine("\n\n6. ADVANCED QUERY PATTERNS");
Console.WriteLine("-".PadRight(60, '-'));

// Using ES|QL functions
Console.WriteLine("\n>> Using ES|QL functions (CIDR match for IPs):");
var ipQuery = Esql.From<ApplicationLog>()
	.Where(l => EsqlFunctions.CidrMatch(l.HostIp!, "10.0.0.0/8"))
	.Take(10);

Console.WriteLine($"   {ipQuery}");

// Combining with generated field names
Console.WriteLine("\n>> Building dynamic query with field constants:");
var indexPattern = Product.Mapping.SearchStrategy.Pattern;
var priceField = Product.Mapping.Fields.Price;
var stockField = Product.Mapping.Fields.InStock;
Console.WriteLine($"   FROM {indexPattern}");
Console.WriteLine($"   | WHERE {stockField} == true");
Console.WriteLine($"   | WHERE {priceField} > 100");
Console.WriteLine($"   | STATS avg_price = AVG({priceField}) BY brand");

// Cross-index correlation (conceptual)
Console.WriteLine("\n>> Cross-index query pattern (orders + logs):");
Console.WriteLine($"   -- Get orders from: {Order.Mapping.SearchStrategy.Pattern}");
Console.WriteLine($"   -- Correlate with logs from: {ApplicationLog.Mapping.IndexStrategy.DataStreamName}");
Console.WriteLine($"   -- Join on: {Order.Mapping.Fields.Id} = labels.orderId");

Console.WriteLine("\n\n" + "=".PadRight(80, '='));
Console.WriteLine("ES|QL queries are generated from LINQ - type-safe and AOT compatible!");
Console.WriteLine("=".PadRight(80, '='));
