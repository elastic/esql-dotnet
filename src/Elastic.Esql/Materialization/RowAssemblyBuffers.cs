// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Materialization;

/// <summary>
/// Scratch space one response enumeration reuses across its rows: the buffers of a row that goes through JSON assembly,
/// and the string cells of a directly bound row with converter cells. Each is created on first use.
/// </summary>
internal sealed class RowAssemblyBuffers(int estimatedRowSize, bool isScalar, bool wrapScalarInArray) : IDisposable
{
	// IDE0032 suggests auto-properties, but the fields exist to defer allocation until a row needs assembly.
#pragma warning disable IDE0032
	private PooledBufferWriter? _rowBuffer;
	private PooledBufferWriter? _valueBuffer;
	private string?[]? _stringCells;
#pragma warning restore IDE0032

	/// <summary>The assembled row object, or the bare cell for scalar reads.</summary>
	public PooledBufferWriter RowBuffer => _rowBuffer ??= new PooledBufferWriter(estimatedRowSize);

	/// <summary>Per-cell scratch for nested layouts, whose cells are regrouped before assembly.</summary>
	public PooledBufferWriter ValueBuffer => _valueBuffer ??= new PooledBufferWriter(estimatedRowSize);

	/// <summary>Whether rows are a single bare cell rather than an assembled JSON object.</summary>
	public bool IsScalar { get; } = isScalar;

	/// <summary>Whether a bare scalar cell is wrapped into a one-element array, for a scalar read whose target is a collection.</summary>
	public bool WrapScalarInArray { get; } = wrapScalarInArray;

	/// <summary>
	/// The string cells of a directly bound row with converter cells, decoded by the validating pass and indexed by column.
	/// The first call sizes the array and later calls return it.
	/// </summary>
	public string?[] StringCells(int columnCount) => _stringCells ??= new string?[columnCount];

	public void Dispose()
	{
		_rowBuffer?.Dispose();
		_valueBuffer?.Dispose();
		_rowBuffer = null;
		_valueBuffer = null;
	}
}
