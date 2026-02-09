// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping;
using MappingAotSmoketest;

Console.WriteLine("Elastic.Mapping AOT Smoketest");
Console.WriteLine(new string('=', 60));

// Access generated context
Console.WriteLine($"Registered types: {AotSmokeContext.All.Count}");

// Product index
Console.WriteLine($"\nProduct Index:");
Console.WriteLine($"  Write Target: {AotSmokeContext.SmokeProduct.IndexStrategy.WriteTarget}");
Console.WriteLine($"  Search Pattern: {AotSmokeContext.SmokeProduct.SearchStrategy.Pattern}");
Console.WriteLine($"  Hash: {AotSmokeContext.SmokeProduct.Hash}");

// Field constants
Console.WriteLine($"\nProduct Fields:");
Console.WriteLine($"  Id: {AotSmokeContext.SmokeProduct.Fields.Id}");
Console.WriteLine($"  Name: {AotSmokeContext.SmokeProduct.Fields.Name}");
Console.WriteLine($"  Price: {AotSmokeContext.SmokeProduct.Fields.Price}");
Console.WriteLine($"  InStock: {AotSmokeContext.SmokeProduct.Fields.InStock}");

// Mappings JSON
var mappingsJson = AotSmokeContext.SmokeProduct.GetMappingJson();
Console.WriteLine($"\nMappings JSON length: {mappingsJson.Length}");
Console.WriteLine($"Mappings JSON preview: {mappingsJson[..Math.Min(200, mappingsJson.Length)]}...");

// Settings JSON
var settingsJson = AotSmokeContext.SmokeProduct.GetSettingsJson();
Console.WriteLine($"\nSettings JSON length: {settingsJson.Length}");

// Complete index JSON
var indexJson = AotSmokeContext.SmokeProduct.GetIndexJson();
Console.WriteLine($"Index JSON length: {indexJson.Length}");

// Log data stream
Console.WriteLine($"\nLog Data Stream:");
Console.WriteLine($"  Name: {AotSmokeContext.SmokeLogEntry.IndexStrategy.DataStreamName}");
Console.WriteLine($"  Type: {AotSmokeContext.SmokeLogEntry.IndexStrategy.Type}");
Console.WriteLine($"  Dataset: {AotSmokeContext.SmokeLogEntry.IndexStrategy.Dataset}");

Console.WriteLine($"\nLog Fields:");
Console.WriteLine($"  Timestamp: {AotSmokeContext.SmokeLogEntry.Fields.Timestamp}");
Console.WriteLine($"  Message: {AotSmokeContext.SmokeLogEntry.Fields.Message}");
Console.WriteLine($"  Level: {AotSmokeContext.SmokeLogEntry.Fields.Level}");

Console.WriteLine("\nAOT smoketest passed!");

namespace MappingAotSmoketest
{
	public class SmokeProduct
	{
		public string Id { get; set; } = null!;
		public string Name { get; set; } = null!;
		public decimal Price { get; set; }
		public bool InStock { get; set; }
		public string? Description { get; set; }
	}

	public class SmokeLogEntry
	{
		public DateTimeOffset Timestamp { get; set; }
		public string Message { get; set; } = null!;
		public string Level { get; set; } = null!;
		public string? Logger { get; set; }
	}

	[ElasticsearchMappingContext]
	[Entity<SmokeProduct>(Target = EntityTarget.Index, Name = "products", SearchPattern = "products*")]
	[Entity<SmokeLogEntry>(Target = EntityTarget.DataStream, Type = "logs", Dataset = "smoketest")]
	public static partial class AotSmokeContext;
}
