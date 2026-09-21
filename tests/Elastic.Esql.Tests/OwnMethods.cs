// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.OwnMethods;

/// <summary>
/// An Any of the caller's own, with the framework's shape but not its meaning. Kept in
/// a namespace of its own, so that no test picks it up by accident.
/// </summary>
public static class OwnQueries
{
	public static bool Any(this IEnumerable<string> tags, Func<string, bool> predicate) => !Enumerable.Any(tags, predicate);
}
