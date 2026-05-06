// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

namespace Elastic.Esql.Translation;

/// <summary>
/// Shared helpers for recognizing and translating <see cref="DenseVector{T}"/> conversions inside
/// LINQ expression trees.
/// </summary>
internal static class DenseVectorTypeHelper
{
	/// <summary>Returns <see langword="true"/> when <paramref name="type"/> is a closed <c>DenseVector&lt;T&gt;</c> type.</summary>
	public static bool IsDenseVectorType(Type type) =>
		type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DenseVector<>);

	/// <summary>
	/// If <paramref name="convert"/> is an implicit / explicit conversion to <c>DenseVector&lt;T&gt;</c>
	/// from a closure-resolvable expression, resolves the value through the conversion's
	/// <c>op_Implicit</c> method and emits it as an inline literal or named parameter via
	/// <paramref name="context"/>. Returns <see langword="false"/> when the operand is a
	/// parameter-rooted member access (regular field-name path) or when the value cannot be resolved.
	/// </summary>
	public static bool TryEmitDenseVectorLiteral(
		UnaryExpression convert,
		EsqlTranslationContext context,
		out string result)
	{
		result = string.Empty;

		if (!IsDenseVectorType(convert.Type))
			return false;

		if (convert.Operand is MemberExpression member && ExpressionTranslationHelpers.IsRootedInParameter(member))
			return false;

		var resolved = ExpressionConstantResolver.Resolve(convert);
		if (resolved is null)
			return false;

		var name = convert.Operand is MemberExpression operandMember ? operandMember.Member.Name : "vector";
		result = context.GetValueOrParameterName(name, resolved);
		return true;
	}
}
