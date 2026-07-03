// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

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
	/// </summary>
	internal string Add(string preferredName, JsonElement value)
	{
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

	/// <summary>All collected parameters keyed by name.</summary>
	public IReadOnlyDictionary<string, JsonElement> Parameters => _parameters;

	/// <summary>Whether any parameters have been collected.</summary>
	public bool HasParameters => _parameters.Count > 0;
}
