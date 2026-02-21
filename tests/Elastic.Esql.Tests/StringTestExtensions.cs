// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests;

internal static class StringTestExtensions
{
	/// <summary>
	/// Normalizes line endings to the current system's native format.
	/// </summary>
	internal static string NativeLineEndings(this string value) =>
		value
			.Replace("\r\n", "\n")
			.Replace("\n", Environment.NewLine);
}
