// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel;

/// <summary>
/// Represents a collection of named parameters for use with ES|QL queries.
/// </summary>
public class EsqlParameters
{
	private readonly Dictionary<string, object?> _parameters = [];
	private readonly Dictionary<string, int> _nameCounts = [];

	// TODO: We should keep track of the "actual" ParameterExpressions to avoid duplicates if the parameter is identical.

	/// <summary>
	/// Adds a parameter and returns its unique name.
	/// Duplicate preferred names get <c>_2</c>, <c>_3</c> suffixes.
	/// </summary>
	internal string Add(string preferredName, object? value)
	{
		if (!_nameCounts.TryGetValue(preferredName, out var count))
		{
			_nameCounts[preferredName] = 1;
			_parameters[preferredName] = value;
			return preferredName;
		}

		var next = count + 1;
		_nameCounts[preferredName] = next;
		var uniqueName = $"{preferredName}_{next}";
		_parameters[uniqueName] = value;
		return uniqueName;
	}

	/// <summary>All collected parameters keyed by name.</summary>
	public IReadOnlyDictionary<string, object?> Parameters => _parameters;

	/// <summary>Whether any parameters have been collected.</summary>
	public bool HasParameters => _parameters.Count > 0;

	// TODO: Move to specific implementation.

	/// <summary>
	/// Converts to ES|QL API format: a list of single-entry dictionaries.
	/// </summary>
	public IReadOnlyList<object> ToEsqlParams() =>
		[.. _parameters.Select(kvp => (object)new Dictionary<string, object?> { [kvp.Key] = kvp.Value })];
}
