// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Tests.QueryModel;

public class CommandDefensiveCopyTests
{
	[Test]
	public void EvalCommand_MutateSourceArray_CommandUnaffected()
	{
		var expressions = new[] { "a = 1", "b = 2" };
		var command = new EvalCommand(expressions);

		expressions[0] = "mutated";

		_ = command.Expressions[0].Should().Be("a = 1");
	}

	[Test]
	public void KeepCommand_MutateSourceArray_CommandUnaffected()
	{
		var fields = new[] { "message", "duration" };
		var command = new KeepCommand(fields);

		fields[0] = "mutated";

		_ = command.Fields[0].Should().Be("message");
	}

	[Test]
	public void DropCommand_MutateSourceArray_CommandUnaffected()
	{
		var fields = new[] { "message", "duration" };
		var command = new DropCommand(fields);

		fields[0] = "mutated";

		_ = command.Fields[0].Should().Be("message");
	}

	[Test]
	public void RowCommand_MutateSourceArray_CommandUnaffected()
	{
		var expressions = new[] { "a = 1" };
		var command = new RowCommand(expressions);

		expressions[0] = "mutated";

		_ = command.Expressions[0].Should().Be("a = 1");
	}

	[Test]
	public void SortCommand_MutateSourceArray_CommandUnaffected()
	{
		var original = new SortField("timestamp", descending: true);
		var fields = new[] { original };
		var command = new SortCommand(fields);

		fields[0] = new SortField("mutated");

		_ = command.Fields[0].Should().BeSameAs(original);
	}

	[Test]
	public void ForkCommand_MutateSourceLists_CommandUnaffected()
	{
		var fragments = new List<string> { "WHERE a > 1", "LIMIT 10" };
		var branches = new List<ForkBranch> { new(fragments, hasLimit: true) };
		var command = new ForkCommand(branches);

		fragments[0] = "mutated";
		branches.Clear();

		_ = command.Branches.Should().HaveCount(1);
		_ = command.Branches[0].Fragments[0].Should().Be("WHERE a > 1");
	}

	[Test]
	public void ForkCommand_NullBranchEntry_ThrowsArgumentException()
	{
		var branches = new List<ForkBranch> { new(["LIMIT 1"], hasLimit: true), null! };

		var act = () => new ForkCommand(branches);

		_ = act.Should().ThrowExactly<ArgumentException>().WithParameterName("branches");
	}

	[Test]
	public void ForkBranch_NullFragmentEntry_ThrowsArgumentException()
	{
		var act = () => new ForkBranch(["LIMIT 1", null!], hasLimit: true);

		_ = act.Should().ThrowExactly<ArgumentException>().WithParameterName("fragments");
	}

	[Test]
	public void FuseCommand_MutateSourceLists_CommandUnaffected()
	{
		var weights = new List<double> { 0.7, 0.3 };
		var keyColumns = new List<string> { "_id" };
		var command = new FuseCommand(FuseMethod.Rrf, weights: weights, keyColumns: keyColumns);

		weights[0] = 99;
		keyColumns[0] = "mutated";

		_ = command.Weights![0].Should().Be(0.7);
		_ = command.KeyColumns![0].Should().Be("_id");
	}
}
