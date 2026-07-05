// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.Extensions;
using EsqlAotSmoketest;

Console.WriteLine("Elastic.Esql AOT Smoketest");
Console.WriteLine(new string('=', 60));

// Create a provider using the source-generated JsonSerializerContext
var provider = new EsqlQueryProvider(EsqlJsonContext.Default);
var namingPolicy = EsqlJsonContext.Default.Options.PropertyNamingPolicy;

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

// Verify field names are resolved via STJ naming policy
Console.WriteLine($"\nField resolution test:");
Console.WriteLine($"  OrderId resolves to: {namingPolicy?.ConvertName("OrderId") ?? "OrderId"}");
Console.WriteLine($"  TotalAmount resolves to: {namingPolicy?.ConvertName("TotalAmount") ?? "TotalAmount"}");

// Materialization: run canned responses through the reader so PublishAot validates
// row and scalar deserialization, not just query translation.
const string rowsJson =
	"""
	{"took":1,"columns":[{"name":"orderId","type":"keyword"},{"name":"status","type":"keyword"},{"name":"totalAmount","type":"double"},{"name":"timestamp","type":"date"}],"values":[["A-1001","Shipped",150.5,"2026-01-01T10:00:00.000Z"],["A-1002","Pending",42.5,"2026-01-02T11:30:00.000Z"]]}
	""";

var rowsProvider = new EsqlQueryProvider(EsqlJsonContext.Default, new StubQueryExecutor(rowsJson));
var orders = new EsqlQueryable<EsqlOrder>(rowsProvider)
	.From("orders")
	.ToList();

if (orders.Count != 2)
	throw new InvalidOperationException($"Expected 2 rows but materialized {orders.Count}.");
if (orders[0].OrderId != "A-1001" || orders[0].Status != "Shipped" || orders[0].TotalAmount != 150.5)
	throw new InvalidOperationException("Row 0 did not materialize the expected values.");
if (orders[1].OrderId != "A-1002" || orders[1].Status != "Pending" || orders[1].TotalAmount != 42.5)
	throw new InvalidOperationException("Row 1 did not materialize the expected values.");

Console.WriteLine($"\nMaterialization test (rows):");
Console.WriteLine($"  Row 0: {orders[0].OrderId} {orders[0].Status}");
Console.WriteLine($"  Row 1: {orders[1].OrderId} {orders[1].Status}");

const string scalarJson =
	"""
	{"took":1,"columns":[{"name":"result","type":"long"}],"values":[[2]]}
	""";

var scalarProvider = new EsqlQueryProvider(EsqlJsonContext.Default, new StubQueryExecutor(scalarJson));
var count = new EsqlQueryable<EsqlOrder>(scalarProvider)
	.From("orders")
	.Count();

if (count != 2)
	throw new InvalidOperationException($"Expected scalar count 2 but got {count}.");

Console.WriteLine($"\nMaterialization test (scalar):");
Console.WriteLine($"  Count = {count}");

// Dotted columns force a nested ColumnLayout, which routes materialization through
// StreamRowsBatched instead of the flat row-at-a-time path - only that path exercises the
// source-gen List<T> resolver under AOT.
const string nestedJson =
	"""
	{"took":1,"columns":[{"name":"shipmentId","type":"keyword"},{"name":"address.city","type":"keyword"},{"name":"address.zip","type":"keyword"}],"values":[["S-1","Berlin","10115"],["S-2","Munich","80331"]]}
	""";

var nestedProvider = new EsqlQueryProvider(EsqlJsonContext.Default, new StubQueryExecutor(nestedJson));
var shipments = new EsqlQueryable<EsqlShipment>(nestedProvider)
	.From("shipments")
	.ToList();

if (shipments.Count != 2)
	throw new InvalidOperationException($"Expected 2 rows but materialized {shipments.Count}.");
if (shipments[0].Address is null || shipments[0].Address!.City != "Berlin")
	throw new InvalidOperationException("Row 0 did not materialize the expected nested values.");

Console.WriteLine($"\nMaterialization test (nested):");
Console.WriteLine($"  Row 0: {shipments[0].ShipmentId} {shipments[0].Address!.City}");
Console.WriteLine($"  Row 1: {shipments[1].ShipmentId} {shipments[1].Address!.City}");

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

	public class EsqlShipment
	{
		public string? ShipmentId { get; set; }
		public ShipmentAddress? Address { get; set; }
	}

	public class ShipmentAddress
	{
		public string? City { get; set; }
		public string? Zip { get; set; }
	}

	[JsonSerializable(typeof(EsqlOrder))]
	[JsonSerializable(typeof(EsqlProduct))]
	[JsonSerializable(typeof(EsqlShipment))]
	[JsonSerializable(typeof(List<EsqlShipment>))]
	[JsonSerializable(typeof(int))]
	[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
	public partial class EsqlJsonContext : JsonSerializerContext;

	/// <summary>Returns a canned ES|QL JSON response for every synchronous query.</summary>
	internal sealed class StubQueryExecutor(string json) : IEsqlQueryExecutor
	{
		public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request) =>
			new StubResponse(json);

		public Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException("The smoketest only exercises synchronous execution.");

		public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request) =>
			throw new NotSupportedException("The smoketest only exercises synchronous execution.");

		public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException("The smoketest only exercises synchronous execution.");

		public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request) =>
			throw new NotSupportedException("The smoketest only exercises synchronous execution.");

		public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException("The smoketest only exercises synchronous execution.");

		public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request) =>
			throw new NotSupportedException("The smoketest only exercises synchronous execution.");

		public Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException("The smoketest only exercises synchronous execution.");
	}

	/// <summary>Wraps a canned JSON payload as a synchronous ES|QL response.</summary>
	internal sealed class StubResponse(string json) : IEsqlResponse
	{
		private readonly MemoryStream _stream = new(Encoding.UTF8.GetBytes(json));

		public Stream Body => _stream;

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}

		public void Dispose() => _stream.Dispose();
	}
}
