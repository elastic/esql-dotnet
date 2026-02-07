// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Esql.QueryModel;
using Elastic.Esql.TypeMapping;

namespace Elastic.Esql.Execution;

/// <summary>
/// Materializes ES|QL query results into C# objects.
/// </summary>
public class ResultMaterializer(FieldNameResolver? fieldNameResolver = null)
{
	private readonly FieldNameResolver _resolver = fieldNameResolver ?? new();

	/// <summary>
	/// Materializes query results to typed objects.
	/// </summary>
	public IEnumerable<T> Materialize<T>(EsqlResponse response, EsqlQuery _)
	{
		if (response.Values.Count == 0)
			yield break;

		var columnMap = BuildColumnMap(response.Columns);
		var type = typeof(T);

		// Handle anonymous types and value tuples
		if (type.IsAnonymousType() || type.IsValueType)
		{
			foreach (var row in response.Values)
				yield return MaterializeAnonymous<T>(row, columnMap, response.Columns);
		}
		else if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
		{
			// Single value
			foreach (var row in response.Values)
			{
				if (row.Count > 0)
					yield return ConvertValue<T>(row[0], response.Columns[0].Type);
			}
		}
		else
		{
			// Complex type
			var propertyMap = BuildPropertyMap<T>();

			foreach (var row in response.Values)
				yield return MaterializeObject<T>(row, columnMap, response.Columns, propertyMap);
		}
	}

	/// <summary>
	/// Materializes a scalar aggregation result.
	/// </summary>
	public T MaterializeScalar<T>(EsqlResponse response)
	{
		if (response.Values.Count == 0 || response.Values[0].Count == 0)
			return default!;

		var value = response.Values[0][0];
		var columnType = response.Columns.Count > 0 ? response.Columns[0].Type : "long";

		return ConvertValue<T>(value, columnType);
	}

	private static Dictionary<string, int> BuildColumnMap(List<EsqlColumn> columns)
	{
		var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < columns.Count; i++)
			map[columns[i].Name] = i;
		return map;
	}

	private Dictionary<string, PropertyInfo> BuildPropertyMap<T>()
	{
		// Try generated property map first (eliminates per-property reflection)
		var generatedMap = _resolver.GetGeneratedPropertyMap(typeof(T));
		if (generatedMap != null)
		{
			// Wrap in case-insensitive dictionary for compatibility
			var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
			foreach (var kvp in generatedMap)
				map[kvp.Key] = kvp.Value;
			return map;
		}

		// Fallback for non-generated types
		return BuildPropertyMapViaReflection<T>();
	}

	private Dictionary<string, PropertyInfo> BuildPropertyMapViaReflection<T>()
	{
		var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
		var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

		foreach (var prop in properties)
		{
			if (_resolver.IsIgnored(prop))
				continue;

			// Get the field name from JsonPropertyName or use camelCase
			var jsonPropertyName = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
			var fieldName = jsonPropertyName?.Name ?? ToCamelCase(prop.Name);

			map[fieldName] = prop;

			// Also map by property name for flexibility
			if (!map.ContainsKey(prop.Name))
				map[prop.Name] = prop;
		}

		return map;
	}

	private static T MaterializeObject<T>(
		List<object?> row,
		Dictionary<string, int> columnMap,
		List<EsqlColumn> columns,
		Dictionary<string, PropertyInfo> propertyMap)
	{
		var instance = Activator.CreateInstance<T>();

		foreach (var (fieldName, columnIndex) in columnMap)
		{
			if (propertyMap.TryGetValue(fieldName, out var property))
			{
				var value = row[columnIndex];
				var columnType = columns[columnIndex].Type;
				var convertedValue = ConvertValue(value, columnType, property.PropertyType);

				property.SetValue(instance, convertedValue);
			}
		}

		return instance;
	}

	private static T MaterializeAnonymous<T>(
		List<object?> row,
		Dictionary<string, int> columnMap,
		List<EsqlColumn> columns)
	{
		var type = typeof(T);

		// For anonymous types, try to match constructor parameters
		var constructor = type.GetConstructors().FirstOrDefault();
		if (constructor != null)
		{
			var parameters = constructor.GetParameters();
			var args = new object?[parameters.Length];

			for (var i = 0; i < parameters.Length; i++)
			{
				var paramName = parameters[i].Name!;

				// Try to find matching column
				if (columnMap.TryGetValue(paramName, out var columnIndex) && columnIndex < row.Count)
				{
					var columnType = columns[columnIndex].Type;
					args[i] = ConvertValue(row[columnIndex], columnType, parameters[i].ParameterType);
				}
				else if (i < row.Count)
				{
					// Fall back to positional
					var columnType = columns[i].Type;
					args[i] = ConvertValue(row[i], columnType, parameters[i].ParameterType);
				}
				else
					args[i] = GetDefault(parameters[i].ParameterType);
			}

			return (T)constructor.Invoke(args);
		}

		return default!;
	}

	private static T ConvertValue<T>(object? value, string _) => (T)ConvertValue(value, typeof(T))!;

	private static object? ConvertValue(object? value, string _, Type targetType) => ConvertValue(value, targetType);

	private static object? ConvertValue(object? value, Type targetType)
	{
		if (value == null)
			return GetDefault(targetType);

		// Handle JsonElement from System.Text.Json
		if (value is JsonElement jsonElement)
		{
			value = ExtractJsonValue(jsonElement);
			if (value == null)
				return GetDefault(targetType);
		}

		var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

		try
		{
			// String
			if (underlyingType == typeof(string))
				return value.ToString();

			// Boolean
			if (underlyingType == typeof(bool))
			{
				if (value is bool b)
					return b;
				if (value is string s)
					return bool.Parse(s);
				return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
			}

			// Numeric types
			if (underlyingType == typeof(int))
				return Convert.ToInt32(value, CultureInfo.InvariantCulture);
			if (underlyingType == typeof(long))
				return Convert.ToInt64(value, CultureInfo.InvariantCulture);
			if (underlyingType == typeof(short))
				return Convert.ToInt16(value, CultureInfo.InvariantCulture);
			if (underlyingType == typeof(byte))
				return Convert.ToByte(value, CultureInfo.InvariantCulture);
			if (underlyingType == typeof(float))
				return Convert.ToSingle(value, CultureInfo.InvariantCulture);
			if (underlyingType == typeof(double))
				return Convert.ToDouble(value, CultureInfo.InvariantCulture);
			if (underlyingType == typeof(decimal))
				return Convert.ToDecimal(value, CultureInfo.InvariantCulture);

			// DateTime
			if (underlyingType == typeof(DateTime))
			{
				if (value is DateTime dt)
					return dt;
				if (value is string s)
					return DateTime.Parse(s, CultureInfo.InvariantCulture);
				if (value is long ticks)
					return DateTimeOffset.FromUnixTimeMilliseconds(ticks).UtcDateTime;
				return Convert.ToDateTime(value, CultureInfo.InvariantCulture);
			}

			if (underlyingType == typeof(DateTimeOffset))
			{
				if (value is DateTimeOffset dto)
					return dto;
				if (value is string s)
					return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);
				if (value is long ticks)
					return DateTimeOffset.FromUnixTimeMilliseconds(ticks);
				return new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture));
			}

			if (underlyingType == typeof(DateOnly))
			{
				if (value is string s)
					return DateOnly.Parse(s, CultureInfo.InvariantCulture);
				if (value is DateTime dt)
					return DateOnly.FromDateTime(dt);
				return DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture));
			}

			if (underlyingType == typeof(TimeOnly))
			{
				if (value is string s)
					return TimeOnly.Parse(s, CultureInfo.InvariantCulture);
				return TimeOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture));
			}

			// Guid
			if (underlyingType == typeof(Guid))
			{
				if (value is Guid g)
					return g;
				if (value is string s)
					return Guid.Parse(s);
			}

			// Enum
			if (underlyingType.IsEnum)
			{
				if (value is string s)
					return Enum.Parse(underlyingType, s, ignoreCase: true);
				return Enum.ToObject(underlyingType, value);
			}

			// Fallback
			return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
		}
		catch
		{
			return GetDefault(targetType);
		}
	}

	private static object? ExtractJsonValue(JsonElement element) =>
		element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number when element.TryGetInt64(out var l) => l,
			JsonValueKind.Number when element.TryGetDouble(out var d) => d,
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null => null,
			JsonValueKind.Array => element.EnumerateArray().Select(ExtractJsonValue).ToList(),
			_ => element.GetRawText()
		};

	private static object? GetDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

	private static string ToCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		if (name.Length == 1)
			return name.ToLowerInvariant();

		return char.ToLowerInvariant(name[0]) + name.Substring(1);
	}
}

internal static class TypeExtensions
{
	public static bool IsAnonymousType(this Type type) =>
		type.Name.StartsWith("<>", StringComparison.OrdinalIgnoreCase) &&
		type.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Length != 0;
}
