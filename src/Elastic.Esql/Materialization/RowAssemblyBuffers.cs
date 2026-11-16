// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Materialization;

/// <summary>
/// Scratch space one response enumeration reuses across its rows: the buffers of a row that goes through JSON assembly
/// or wraps a bare scalar converter cell, and the string cells and string list cells of a directly bound row that takes
/// the validating pass. Each is created on first use.
/// </summary>
internal sealed class RowAssemblyBuffers(int estimatedRowSize, bool isScalar, bool wrapScalarInArray) : IDisposable
{
	// IDE0032 suggests auto-properties, but the fields exist to defer allocation until a row needs assembly or a string
	// array cell.
#pragma warning disable IDE0032
	private PooledBufferWriter? _rowBuffer;
	private PooledBufferWriter? _valueBuffer;
	private List<string?>? _stringListScratch;
#pragma warning restore IDE0032
	private string?[]? _stringCells;
	private object?[]? _listCells;

	/// <summary>The assembled row object, or the bare cell for scalar reads.</summary>
	public PooledBufferWriter RowBuffer => _rowBuffer ??= new PooledBufferWriter(estimatedRowSize);

	/// <summary>Per-cell scratch for nested layouts, whose cells are regrouped before assembly.</summary>
	public PooledBufferWriter ValueBuffer => _valueBuffer ??= new PooledBufferWriter(estimatedRowSize);

	/// <summary>Whether rows are a single bare cell rather than an assembled JSON object.</summary>
	public bool IsScalar { get; } = isScalar;

	/// <summary>Whether a bare scalar cell is wrapped into a one-element array, for a scalar read whose target is a collection.</summary>
	public bool WrapScalarInArray { get; } = wrapScalarInArray;

	/// <summary>
	/// The string cells of a directly bound row that takes the validating pass, indexed by column and decoded once that
	/// pass has seen the whole row. The first call sizes the array and later calls return it.
	/// </summary>
	public string?[] StringCells(int columnCount) => _stringCells ??= new string?[columnCount];

	/// <summary>
	/// The decoded <c>List&lt;string&gt;</c> or <c>string[]</c> of each string list cell of a directly bound row, indexed by
	/// column like <see cref="StringCells"/>. The first call sizes the array and later calls return it.
	/// </summary>
	public object?[] ListCells(int columnCount) => _listCells ??= new object?[columnCount];

	/// <summary>Collects the elements of a <c>string[]</c> cell, whose array is sized once they are all read.</summary>
	public List<string?> StringListScratch => _stringListScratch ??= [];

	/// <summary>Clears <see cref="StringListScratch"/> where a row has created it.</summary>
	public void ClearStringListScratch() => _stringListScratch?.Clear();

	public void Dispose()
	{
		_rowBuffer?.Dispose();
		_valueBuffer?.Dispose();
		_rowBuffer = null;
		_valueBuffer = null;
	}
}
