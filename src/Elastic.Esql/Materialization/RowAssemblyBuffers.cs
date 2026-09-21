// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Text.Json;

namespace Elastic.Esql.Materialization;

/// <summary>
/// The scratch writers one response enumeration reuses for every row: the assembled row JSON,
/// the per-cell value scratch, and the optional scalar writer for single-column reads.
/// </summary>
internal sealed class RowAssemblyBuffers(
	ArrayBufferWriter<byte> rowBuffer,
	ArrayBufferWriter<byte>? valueBuffer,
	Utf8JsonWriter? valueWriter,
	Utf8JsonWriter? scalarWriter)
{
	public ArrayBufferWriter<byte> RowBuffer { get; } = rowBuffer;

	public ArrayBufferWriter<byte>? ValueBuffer { get; } = valueBuffer;

	public Utf8JsonWriter? ValueWriter { get; } = valueWriter;

	public Utf8JsonWriter? ScalarWriter { get; } = scalarWriter;
}
