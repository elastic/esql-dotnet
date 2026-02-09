// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Mapping;
using EsqlAotSmoketest;

Console.WriteLine("Elastic.Esql AOT Smoketest");
Console.WriteLine(new string('=', 60));

// Verify context registration
Console.WriteLine($"Registered types: {EsqlAotContext.All.Count}");

// Build a LINQ query and get the ES|QL string
var query = new EsqlQueryable<EsqlOrder>(EsqlAotContext.Instance)
	.Where(o => o.TotalAmount > 100)
	.Where(o => o.Status == "Shipped")
	.OrderByDescending(o => o.TotalAmount)
	.Take(10);

var esql = query.ToEsqlString();
Console.WriteLine($"\nGenerated ES|QL query:");
Console.WriteLine($"  {esql}");

// Another query with Select
var projectionQuery = new EsqlQueryable<EsqlOrder>(EsqlAotContext.Instance)
	.Where(o => o.TotalAmount > 50)
	.Select(o => new { o.OrderId, o.Status, o.TotalAmount });

var projectionEsql = projectionQuery.ToEsqlString();
Console.WriteLine($"\nProjection query:");
Console.WriteLine($"  {projectionEsql}");

// Product query
var productQuery = new EsqlQueryable<EsqlProduct>(EsqlAotContext.Instance)
	.Where(p => p.InStock)
	.OrderBy(p => p.Price)
	.Take(5);

var productEsql = productQuery.ToEsqlString();
Console.WriteLine($"\nProduct query:");
Console.WriteLine($"  {productEsql}");

// Verify field constants
Console.WriteLine($"\nOrder Fields:");
Console.WriteLine($"  OrderId: {EsqlAotContext.EsqlOrder.Fields.OrderId}");
Console.WriteLine($"  Status: {EsqlAotContext.EsqlOrder.Fields.Status}");
Console.WriteLine($"  TotalAmount: {EsqlAotContext.EsqlOrder.Fields.TotalAmount}");

Console.WriteLine($"\nProduct Fields:");
Console.WriteLine($"  Name: {EsqlAotContext.EsqlProduct.Fields.Name}");
Console.WriteLine($"  Price: {EsqlAotContext.EsqlProduct.Fields.Price}");

Console.WriteLine("\nAOT smoketest passed!");

namespace EsqlAotSmoketest
{
	public class EsqlOrder
	{
		public string OrderId { get; set; } = null!;
		public string Status { get; set; } = null!;
		public decimal TotalAmount { get; set; }
		public DateTimeOffset Timestamp { get; set; }
	}

	public class EsqlProduct
	{
		public string Id { get; set; } = null!;
		public string Name { get; set; } = null!;
		public decimal Price { get; set; }
		public bool InStock { get; set; }
	}

	[ElasticsearchMappingContext]
	[Entity<EsqlOrder>(Target = EntityTarget.Index, Name = "orders", SearchPattern = "orders-*")]
	[Entity<EsqlProduct>(Target = EntityTarget.Index, Name = "products", SearchPattern = "products*")]
	public static partial class EsqlAotContext;
}
