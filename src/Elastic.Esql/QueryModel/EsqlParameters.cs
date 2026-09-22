// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Validation;

namespace Elastic.Esql.QueryModel;

/// <summary>
/// Represents a collection of named parameters for use with ES|QL queries.
/// </summary>
public sealed class EsqlParameters
{
	private readonly Dictionary<string, JsonElement> _parameters = [];
	private readonly Dictionary<string, int> _nameCounts = [];

	// TODO: We should keep track of the "actual" ParameterExpressions to avoid duplicates if the parameter is identical.

	/// <summary>
	/// Adds a parameter and returns its unique name.
	/// Duplicate preferred names get <c>_2</c>, <c>_3</c> suffixes.
	/// Reference the returned name as <c>?name</c> in command text (e.g. a <see cref="Commands.WhereCommand"/> condition).
	/// </summary>
	/// <param name="preferredName">The preferred parameter name.</param>
	/// <param name="value">The parameter value, pre-serialized as a <see cref="JsonElement"/>.</param>
	/// <returns>The unique name under which the parameter was registered.</returns>
	public string Add(string preferredName, JsonElement value)
	{
		Verify.NotNullOrEmpty(preferredName);

		if (!IsValidParameterName(preferredName))
			throw new ArgumentException(
				"An ES|QL parameter name must start with a letter or underscore and may contain only ASCII letters, digits, and underscores.",
				nameof(preferredName));

		// A caller's element may belong to a JsonDocument that is disposed before the query executes; the
		// clone is independent of it, and cloning an already independent element allocates nothing.
		value = value.Clone();

		if (!_nameCounts.TryGetValue(preferredName, out var count) && !_parameters.ContainsKey(preferredName))
		{
			_nameCounts[preferredName] = 1;
			_parameters.Add(preferredName, value);
			return preferredName;
		}

		// Suffixes start at _2. Skip names already taken by other parameters, e.g. a captured
		// variable literally named "id_2" that would otherwise be overwritten by a duplicated "id".
		var next = count > 0 ? count + 1 : 2;
		var uniqueName = $"{preferredName}_{next}";
		while (_parameters.ContainsKey(uniqueName))
		{
			next++;
			uniqueName = $"{preferredName}_{next}";
		}

		_nameCounts[preferredName] = next;
		_parameters.Add(uniqueName, value);
		return uniqueName;
	}

	// Mirrors the ES|QL lexer rule for named parameters (a letter or underscore, then letters, digits, or
	// underscores, all ASCII) so a bad name fails here instead of as a server-side parse error.
	private static bool IsValidParameterName(string name)
	{
		if (!IsAsciiLetterOrUnderscore(name[0]))
			return false;

		for (var i = 1; i < name.Length; i++)
		{
			if (!IsAsciiLetterOrUnderscore(name[i]) && name[i] is not (>= '0' and <= '9'))
				return false;
		}

		return true;
	}

	private static bool IsAsciiLetterOrUnderscore(char c) =>
		c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';

	/// <summary>All collected parameters keyed by name.</summary>
	public IReadOnlyDictionary<string, JsonElement> Parameters => _parameters;

	/// <summary>Whether any parameters have been collected.</summary>
	public bool HasParameters => _parameters.Count > 0;
}
