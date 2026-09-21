// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>
/// Overloads and values the translation cannot honour. Each one has to be refused
/// rather than translated into a predicate that quietly means something else.
/// </summary>
public class UnsupportedOverloadTests : EsqlTestBase
{
	[Test]
	public void CompareWithACultureSensitiveComparison_IsRefused()
	{
		// an ordinal comparison is what ES|QL performs; a culture-sensitive one asks
		// for a different ordering and is refused rather than answered with this one
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.Message, "m", StringComparison.CurrentCulture) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void CompareToAnObject_IsRefused()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.CompareTo((object)"m") > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*CompareOrdinal*");
	}

	[Test]
	public void CompareTo_IsRefusedForOrderingByCulture()
	{
		// CompareTo orders by the current culture, where "B" sorts after "a"; a keyword
		// field is ordered by its UTF-8 bytes, which is what CompareOrdinal asks for
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.CompareTo("m") > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*CompareOrdinal*");
	}

	[Test]
	public void TwoArgumentCompare_IsRefusedForOrderingByCulture()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.Message, "m") > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*CompareOrdinal*");
	}

	[Test]
	public void CompareOrdinalOverARange_IsRefused()
	{
		// ordinal, but over a substring of each operand: not the ordering of the field
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, 0, "m", 0, 1) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*overload*");
	}

	[Test]
	public void CompareOrdinalOutsideAComparisonAgainstZero_IsRefused()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, "m") == 1);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*against zero*");
	}

	[Test]
	public void ContainsWithAComparer_IsRefused()
	{
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("x", StringComparer.OrdinalIgnoreCase));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void AnElementComparedToNull_IsRefused()
	{
		// a multi-value field stores no null element, and MATCH(field, null) is not valid
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == null));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void AllElementsComparedToNull_IsRefused()
	{
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.All(t => t != null));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void ContainsNull_IsRefused()
	{
		var missing = (string?)null;

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains(missing!));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void CompareOrdinalAgainstNull_IsRefused()
	{
		// .NET orders a non-null string above null; an ES|QL comparison against null
		// does not reproduce that
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, (string?)null) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*IS NULL*");
	}

	[Test]
	public void CompareOrdinalAgainstACapturedNull_IsRefused()
	{
		var missing = (string?)null;

		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.CompareOrdinal(l.Message, missing) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*IS NULL*");
	}

	[Test]
	public void AConstantCollectionContainsWithAComparer_IsRefused()
	{
		var wanted = new[] { "a", "b" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t, StringComparer.OrdinalIgnoreCase)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void AConstantCollectionHoldingNull_IsRefused()
	{
		// a stored value is never null, so there is nothing for MATCH to match
		var candidates = new string?[] { null, "x" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => candidates.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void ANullComparisonAgainstAProjectedValue_IsRefused()
	{
		// after a projection the parameter is the projected scalar, which has no field
		// name of its own and may well be null
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.ClientIp)
			.Where(ip => ip == null);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void CompareWithOrdinalIgnoreCase_IsRefused()
	{
		// keyword ordering is case-sensitive: "a" sorts after "B" there, and before it
		// under OrdinalIgnoreCase, so the two disagree
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.Message, "m", StringComparison.OrdinalIgnoreCase) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void ANullComparisonAfterAnyProjection_IsRefused()
	{
		// the projected value may be null even when its type matches the document's,
		// which is what a self-referencing type does
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l.Message)
			.Where(message => message == null);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void ASetWithItsOwnComparer_IsRefused()
	{
		// the set answers Contains by its comparer, which the emitted comparison does not
		var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IOT" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void ASetTypedField_IsRefused()
	{
		// the field's declared type is a set: it answers Contains by a comparer the
		// translation cannot see, so it is refused like a set-typed constant
		var query = CreateQuery<SetTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*set answers Contains*");
	}

	[Test]
	public void ContainsOverAnInterfaceTypedField_IsRefused()
	{
		// the interface says nothing about the collection behind the field, which is what
		// answers Contains; Any compares the elements themselves and is the shape to use
		var query = CreateQuery<InterfaceTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Contains("iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Any(t => t == value)*");
	}

	[Test]
	public void AnyOverAnInterfaceTypedField_IsStillTranslated()
	{
		var esql = CreateQuery<InterfaceTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot"))
			.ToString();

		_ = esql.Should().Contain("MATCH(tags, \"iot\")");
	}

	[Test]
	public void AFieldOfACollectionTypeOfItsOwn_IsRefused()
	{
		// the declared type says nothing about how the collection answers Contains, so
		// only an array or a list of the base library is taken
		var query = CreateQuery<OwnTaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => t == "iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*collection type of your own*");
	}

	[Test]
	public void AnAnyOfTheCallersOwn_IsNotTakenForTheFrameworkOne()
	{
		// a method named Any that is not Enumerable.Any may mean anything: it is left to
		// fail soft rather than translated as the framework's
		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => OwnMethods.OwnQueries.Any(p.Tags, t => t == "iot"));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*not supported*");
	}

	[Test]
	public void AFrozenSetWithItsOwnComparer_IsRefused()
	{
		// a set of any kind carries its own comparer, not only the ones known by name
		var wanted = new[] { "IOT" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*default equality*");
	}

	[Test]
	public void ACollectionTypeOfTheCallersOwn_IsRefused()
	{
		// its Contains may compare in any way at all; only the known kinds are taken to
		// compare with default equality
		var wanted = new NamedHosts { new NamedHost("IOT") };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*default equality*");
	}

	[Test]
	public void AReadOnlyWrapper_IsRefusedForWhatItMayWrap()
	{
		// ReadOnlyCollection hands Contains to the list it wraps, which may be a list of
		// the caller's own with an equality of its own
		var wanted = new ReadOnlyCollection<string>(new List<string> { "iot" });

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*default equality*");
	}

	[Test]
	public void ALinqQuery_IsTranslated()
	{
		// the LINQ operators compare with default equality
		var wanted = new[] { "IOT" }.Select(tag => tag.ToLowerInvariant());

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Contain("MATCH(tags, \"iot\")");
	}

	[Test]
	public void AnImmutableArray_IsTranslated()
	{
		var wanted = ImmutableArray.Create("iot");

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Contain("MATCH(tags, \"iot\")");
	}

	[Test]
	public void AListWithDefaultEquality_IsStillTranslated()
	{
		// a list has no equality of its own, so membership is plain equality
		var wanted = new List<string> { "iot" };

		var esql = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)))
			.ToString();

		_ = esql.Should().Contain("MATCH(tags, \"iot\")");
	}

	[Test]
	public void ASetWithDefaultEquality_IsRefusedAllTheSame()
	{
		// reading a set's comparer back takes reflection the trimmer cannot see through,
		// so the whole family is left untranslated rather than answered on a guess
		var wanted = new HashSet<string> { "iot" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void ASortedSet_FailsSoftRatherThanThrowingInternally()
	{
		// its Comparer is an IComparer, not an IEqualityComparer: the translation simply
		// cannot read membership from it, and says so like any other unsupported shape
		var wanted = new SortedSet<string> { "iot" };

		var query = CreateQuery<TaggedProduct>()
			.From("products")
			.Where(p => p.Tags.Any(t => wanted.Contains(t)));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void CompareBetweenTwoNullableFields_IsRefused()
	{
		// two absent values have no single ES|QL form for their ordering
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.ClientIp, l.ServerName, StringComparison.Ordinal) < 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*two fields*");
	}

	[Test]
	public void CompareWithAnIgnoreCaseFlag_IsRefusedForTheRightReason()
	{
		// the shape is the supported one, only the overload is not: the message says so
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.Message, "m", true) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*overload*");
	}

	[Test]
	public void CompareAgainstNull_IsRefusedForTheRightReason()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => string.Compare(l.Message, null, StringComparison.Ordinal) > 0);

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*IS NULL*");
	}
}

/// <summary>A collection type of the caller's own, whose Contains compares by a key of its choosing.</summary>
public sealed class NamedHosts() : KeyedCollection<string, NamedHost>(StringComparer.OrdinalIgnoreCase)
{
	protected override string GetKeyForItem(NamedHost item) => item.Name;
}

public sealed record NamedHost(string Name);
