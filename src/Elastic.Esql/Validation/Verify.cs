// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Runtime.CompilerServices;

namespace Elastic.Esql.Validation;

internal static class Verify
{
	/// <summary>
	/// Throws <see cref="ArgumentNullException"/> if <paramref name="value"/> is <see langword="null"/>.
	/// </summary>
	public static void NotNull<T>(
		[System.Diagnostics.CodeAnalysis.NotNull] T? value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null)
#if NET6_0_OR_GREATER
			=> ArgumentNullException.ThrowIfNull(value, paramName);
#else
	{
		if (value is null)
			throw new ArgumentNullException(paramName);
	}
#endif

	/// <summary>
	/// Throws <see cref="ArgumentNullException"/> if <paramref name="value"/> is <see langword="null"/>,
	/// or <see cref="ArgumentException"/> if it is empty.
	/// </summary>
	public static void NotNullOrEmpty(
		[System.Diagnostics.CodeAnalysis.NotNull] string? value,
		[CallerArgumentExpression(nameof(value))] string? paramName = null)
	{
#if NET7_0_OR_GREATER
		ArgumentException.ThrowIfNullOrEmpty(value, paramName);
#else
		if (value is null)
			throw new ArgumentNullException(paramName);

		if (value.Length == 0)
			throw new ArgumentException("The value cannot be an empty string.", paramName);
#endif
	}
}
