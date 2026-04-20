// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Esql.Execution;

namespace Elastic.Esql.Integration.Tests.Esql;

/// <summary>
/// Verifies that server-side ES|QL failures surface through <see cref="EsqlExecutionException"/>
/// with the status code and server-error payload populated by <c>EsqlProductRegistration</c>.
/// </summary>
public class ErrorHandlingTests : IntegrationTestBase
{
	[Test]
	public void InvalidSyntax_Sync_ThrowsEsqlExecutionException()
	{
		var act = () => Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.RawEsql("| BOGUS_COMMAND")
			.AsEsqlQueryable()
			.ToList();

		var ex = act.Should().Throw<EsqlExecutionException>().Which;

		ex.StatusCode.Should().Be(400);
		ex.ResponseBody.Should().NotBeNullOrEmpty();
		ex.ApiCallDetails.Should().NotBeNull();
		ex.ServerError.Should().NotBeNull();
		ex.ServerError!.Error?.Type.Should().BeOneOf("parsing_exception", "verification_exception");
	}

	[Test]
	public void UnknownField_Sync_ThrowsEsqlExecutionException()
	{
		var act = () => Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.RawEsql("| WHERE nonexistent_field == 1")
			.AsEsqlQueryable()
			.ToList();

		var ex = act.Should().Throw<EsqlExecutionException>().Which;

		ex.StatusCode.Should().Be(400);
		ex.ResponseBody.Should().NotBeNullOrEmpty();
		ex.ServerError.Should().NotBeNull();
		ex.ServerError!.Error?.Type.Should().Be("verification_exception");
	}

	[Test]
	public async Task InvalidSyntax_AsyncSubmit_ThrowsEsqlExecutionException()
	{
		var act = async () =>
		{
			await using var asyncQuery = await Fixture.EsqlClient
				.SubmitAsyncQueryAsync<TestProduct>(
					q => q.From(TestDataSeeder.ProductIndex).RawEsql("| BOGUS_COMMAND")
				);
		};

		var ex = (await act.Should().ThrowAsync<EsqlExecutionException>()).Which;

		ex.StatusCode.Should().Be(400);
		ex.ResponseBody.Should().NotBeNullOrEmpty();
		ex.ServerError.Should().NotBeNull();
		ex.ServerError!.Error?.Type.Should().BeOneOf("parsing_exception", "verification_exception");
	}

	[Test]
	public async Task NonExistentIndex_AsyncSubmit_ThrowsEsqlExecutionException()
	{
		var act = async () =>
		{
			await using var asyncQuery = await Fixture.EsqlClient
				.SubmitAsyncQueryAsync<TestProduct>(
					q => q.From("non-existent-index-xyz")
				);
		};

		var ex = (await act.Should().ThrowAsync<EsqlExecutionException>()).Which;

		ex.StatusCode.Should().NotBeNull().And.BeOneOf(400, 404);
		ex.ResponseBody.Should().NotBeNullOrEmpty();
		ex.ServerError.Should().NotBeNull();
		ex.ServerError!.Error?.Type.Should().Be("verification_exception");
	}
}
