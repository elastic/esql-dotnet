// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel;

/// <summary>Collects named parameters during ES|QL query translation.</summary>
public class EsqlParameters
{
	private readonly Dictionary<string, object?> _parameters = [];
	private readonly Dictionary<string, int> _nameCounts = [];

	/// <summary>
	/// Adds a parameter and returns its unique name.
	/// Duplicate preferred names get <c>_2</c>, <c>_3</c> suffixes.
	/// </summary>
	public string Add(string preferredName, object? value)
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

	/// <summary>
	/// Converts to ES|QL API format: a list of single-entry dictionaries.
	/// </summary>
	public List<object> ToEsqlParams() =>
		_parameters.Select(kvp => (object)new Dictionary<string, object?> { [kvp.Key] = kvp.Value }).ToList();
}
