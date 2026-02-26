// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.FieldMetadataResolver;
using Elastic.Mapping;
using EsqlAotSmoketest;

Console.WriteLine("Elastic.Esql AOT Smoketest");
Console.WriteLine(new string('=', 60));

// Verify context registration
Console.WriteLine($"Registered types: {EsqlAotContext.All.Count}");

// Create a field resolver that uses the mapping context
var resolver = new MappingResolver(EsqlAotContext.Instance);
var provider = new EsqlQueryProvider(resolver);

// Build a LINQ query and get the ES|QL string
var query = new EsqlQueryable<EsqlOrder>(provider)
	.Where(o => o.TotalAmount > 100)
	.Where(o => o.Status == "Shipped")
	.OrderByDescending(o => o.TotalAmount)
	.Take(10);

var esql = query.ToEsqlString();
Console.WriteLine($"\nGenerated ES|QL query:");
Console.WriteLine($"  {esql}");

// Keep overload A — simple field selection (fully AOT-safe, no anonymous types)
var keepQuery = new EsqlQueryable<EsqlOrder>(provider)
	.Where(o => o.TotalAmount > 50)
	.Keep(o => o.OrderId, o => o.Status, o => o.TotalAmount);

var keepEsql = keepQuery.ToEsqlString();
Console.WriteLine($"\nKeep query (simple):");
Console.WriteLine($"  {keepEsql}");

// Keep overload B — projection with aliases (AOT-annotated on our side)
var aliasEsql = KeepProjectionQuery(provider);
Console.WriteLine($"\nKeep query (projection with alias):");
Console.WriteLine($"  {aliasEsql}");

// Product query
var productQuery = new EsqlQueryable<EsqlProduct>(provider)
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

// Expression.New with MemberInfo[] has [RequiresUnreferencedCode] — the Keep<T,TResult> overload
// suppresses IL2026 internally, but the C# compiler still emits Expression.New at the call site.
[UnconditionalSuppressMessage("Trimming", "IL2026")]
static string KeepProjectionQuery(EsqlQueryProvider provider) =>
	new EsqlQueryable<EsqlOrder>(provider)
		.Where(o => o.TotalAmount > 50)
		.Keep(o => new { o.OrderId, Amount = o.TotalAmount })
		.ToEsqlString();

namespace EsqlAotSmoketest
{
	public class EsqlOrder
	{
		public string OrderId { get; set; } = null!;
		public string Status { get; set; } = null!;
		public double TotalAmount { get; set; }
		public DateTimeOffset Timestamp { get; set; }
	}

	public class EsqlProduct
	{
		public string Id { get; set; } = null!;
		public string Name { get; set; } = null!;
		public double Price { get; set; }
		public bool InStock { get; set; }
	}

	[ElasticsearchMappingContext]
	[Entity<EsqlOrder>(Target = EntityTarget.Index, Name = "orders", SearchPattern = "orders-*")]
	[Entity<EsqlProduct>(Target = EntityTarget.Index, Name = "products", SearchPattern = "products*")]
	public static partial class EsqlAotContext;

	/// <summary>
	/// Simple <see cref="IEsqlFieldMetadataResolver"/> adapter for <see cref="TypeFieldMetadataResolver"/>.
	/// </summary>
	public sealed class MappingResolver(IElasticsearchMappingContext? context = null) : IEsqlFieldMetadataResolver
	{
		private readonly TypeFieldMetadataResolver _resolver = new(context);

		public string GetFieldName(Type type, MemberInfo member) =>
			_resolver.Resolve(member);

		public string GetAnonymousFieldName(string name) =>
			JsonNamingPolicy.CamelCase.ConvertName(name);
	}
}
