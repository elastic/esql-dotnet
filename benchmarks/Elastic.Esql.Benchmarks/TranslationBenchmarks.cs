// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.Functions;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Benchmarks;

[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public class TranslationBenchmarks
{
	private EsqlQueryProvider _provider = null!;
	private EsqlResponseReader _reader = null!;
	private byte[] _scalarPayload = null!;

	[GlobalSetup]
	public void Setup()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = BenchmarkJsonContext.Default
		};
		_provider = new EsqlQueryProvider(options);
		_reader = new EsqlResponseReader(new JsonMetadataManager(options));
		_scalarPayload = """{"columns":[{"name":"count","type":"integer"}],"values":[[42]]}"""u8.ToArray();
	}

	[Benchmark(Baseline = true)]
	public string Translate_Simple() =>
		new EsqlQueryable<FlatDocument>(_provider)
			.From("bench-*")
			.Where(d => d.Count > 10)
			.Take(100)
			.ToEsqlString();

	[Benchmark]
	public string Translate_Complex()
	{
		var minScore = 10.5;
		var category = "cat-1";

		return new EsqlQueryable<FlatDocument>(_provider)
			.From("bench-*")
			.Where(d => EsqlFunctions.Round(EsqlFunctions.Abs(d.Score), 2) > minScore && d.Category == category)
			.OrderByDescending(d => d.Count)
			.Select(d => new { d.Name, d.Score })
			.Take(50)
			.ToEsqlString(inlineParameters: false);
	}

	[Benchmark]
	public int ReadScalar_Int()
	{
		using var stream = new MemoryStream(_scalarPayload, writable: false);
		return _reader.ReadScalar<int>(stream).Value;
	}
}
