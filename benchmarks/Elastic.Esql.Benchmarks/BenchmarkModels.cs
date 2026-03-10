// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Benchmarks;

[JsonSerializable(typeof(FlatDocument))]
[JsonSerializable(typeof(NestedDocument))]
[JsonSerializable(typeof(DeepDocument))]
[JsonSerializable(typeof(WideDocument))]
[JsonSerializable(typeof(MixedDocument))]
[JsonSerializable(typeof(List<FlatDocument>))]
[JsonSerializable(typeof(List<NestedDocument>))]
[JsonSerializable(typeof(List<DeepDocument>))]
[JsonSerializable(typeof(List<WideDocument>))]
[JsonSerializable(typeof(List<MixedDocument>))]
public sealed partial class BenchmarkJsonContext : JsonSerializerContext;

public class FlatDocument
{
	public string Name { get; set; } = string.Empty;
	public int Count { get; set; }
	public double Score { get; set; }
	public bool Active { get; set; }
	public string Category { get; set; } = string.Empty;
}

public class NestedAddress
{
	public string Street { get; set; } = string.Empty;
	public string City { get; set; } = string.Empty;
}

public class NestedDocument
{
	public string Name { get; set; } = string.Empty;
	public int Age { get; set; }
	public NestedAddress? Address { get; set; }
}

public class DeepLevel3
{
	public string Value { get; set; } = string.Empty;
}

public class DeepLevel2
{
	public string Label { get; set; } = string.Empty;
	public DeepLevel3? Inner { get; set; }
}

public class DeepLevel1
{
	public string Tag { get; set; } = string.Empty;
	public DeepLevel2? Child { get; set; }
}

public class DeepDocument
{
	public string Name { get; set; } = string.Empty;
	public DeepLevel1? Level1 { get; set; }
}

public class WideDocument
{
	public string Field1 { get; set; } = string.Empty;
	public int Field2 { get; set; }
	public double Field3 { get; set; }
	public bool Field4 { get; set; }
	public string Field5 { get; set; } = string.Empty;
	public int Field6 { get; set; }
	public double Field7 { get; set; }
	public bool Field8 { get; set; }
	public string Field9 { get; set; } = string.Empty;
	public int Field10 { get; set; }
}

public class MixedContact
{
	public string Email { get; set; } = string.Empty;
	public string Phone { get; set; } = string.Empty;
}

public class MixedDocument
{
	public string Name { get; set; } = string.Empty;
	public int Age { get; set; }
	public NestedAddress? Address { get; set; }
	public MixedContact? Contact { get; set; }
}
