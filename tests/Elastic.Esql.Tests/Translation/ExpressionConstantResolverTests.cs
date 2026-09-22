// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using Elastic.Esql.Translation;

namespace Elastic.Esql.Tests.Translation;

public class ExpressionConstantResolverTests
{
	[Test]
	public void Resolve_HasValueOnNullNullableCapture_ReturnsFalse()
	{
		var value = (int?)null;
		Expression<Func<bool>> expression = () => value.HasValue;

		var result = ExpressionConstantResolver.Resolve(expression.Body);

		_ = result.Should().Be(false);
	}

	[Test]
	public void Resolve_HasValueOnPopulatedNullableCapture_ReturnsTrue()
	{
		var value = (int?)5;
		Expression<Func<bool>> expression = () => value.HasValue;

		var result = ExpressionConstantResolver.Resolve(expression.Body);

		_ = result.Should().Be(true);
	}

	[Test]
	public void Resolve_ValueOnNullNullableCapture_ThrowsInvalidOperation()
	{
		var value = (int?)null;
		Expression<Func<int>> expression = () => value!.Value;

		var act = () => ExpressionConstantResolver.Resolve(expression.Body);

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("*evaluated to null*");
	}
}
