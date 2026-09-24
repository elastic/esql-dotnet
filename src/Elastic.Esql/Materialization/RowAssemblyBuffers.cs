// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Materialization;

/// <summary>
/// Scratch space one response enumeration reuses for every row that goes through JSON assembly. Buffers are
/// created on first use, so an enumeration whose rows all bind directly never allocates them.
/// </summary>
internal sealed class RowAssemblyBuffers(int estimatedRowSize, bool isScalar, bool wrapScalarInArray, bool needsValueBuffer) : IDisposable
{
	// IDE0032 suggests auto-properties, but the fields exist to defer allocation until a row needs assembly.
#pragma warning disable IDE0032
	private PooledBufferWriter? _rowBuffer;
	private PooledBufferWriter? _valueBuffer;
#pragma warning restore IDE0032

	/// <summary>The assembled row object, or the bare cell for scalar reads.</summary>
	public PooledBufferWriter RowBuffer => _rowBuffer ??= new PooledBufferWriter(estimatedRowSize);

	/// <summary>Per-cell scratch for nested layouts, whose cells are regrouped before assembly; null for flat and scalar reads.</summary>
	public PooledBufferWriter? ValueBuffer => needsValueBuffer ? _valueBuffer ??= new PooledBufferWriter(estimatedRowSize) : null;

	/// <summary>Whether rows are a single bare cell rather than an assembled JSON object.</summary>
	public bool IsScalar { get; } = isScalar;

	/// <summary>Whether a bare scalar cell is wrapped into a one-element array, for a scalar read whose target is a collection.</summary>
	public bool WrapScalarInArray { get; } = wrapScalarInArray;

	public void Dispose()
	{
		_rowBuffer?.Dispose();
		_valueBuffer?.Dispose();
		_rowBuffer = null;
		_valueBuffer = null;
	}
}
