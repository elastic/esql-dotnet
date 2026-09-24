// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Materialization;

internal sealed partial class EsqlResponseReader
{
	/// <summary>
	/// Reads one row's cells directly off the reader and assigns them via cached
	/// <see cref="JsonPropertyInfo.Set"/> delegates. The reader must have just consumed the row's
	/// StartArray token. Returns false with <paramref name="incomplete"/> set when the buffer ends
	/// mid-row (caller re-reads with more data), or false with it unset when a cell's token shape
	/// requires the serializer's coercion or error semantics (caller falls back for this row).
	/// </summary>
	internal static bool TryBindRowDirect<T>(
		ref Utf8JsonReader reader,
		DirectRowBinder binder,
		out T? item,
		out bool incomplete)
	{
		item = default;
		incomplete = false;

		var kinds = binder.Kinds;
		var properties = binder.Properties;
		var typedSetters = binder.TypedSetters;
		var isRequired = binder.IsRequired;

		var instance = binder.CreateObject();

		for (var i = 0; i < kinds.Length; i++)
		{
			if (!reader.Read())
			{
				incomplete = true;
				return false;
			}

			var tokenType = reader.TokenType;

			// Fewer cells than columns - the slow path raises the canonical JsonException.
			if (tokenType == JsonTokenType.EndArray)
				return false;

			if (tokenType == JsonTokenType.Null)
			{
				// A null cell leaves the property at its initializer, as the assembled row omits it. For a
				// required member that omission is the serializer's error to raise.
				if (isRequired[i])
					return false;
				continue;
			}

			if (kinds[i] == DirectBinderKind.Converter)
			{
				if (!TryBindConverterCell(ref reader, tokenType, binder, i, instance, out incomplete))
					return false;
				continue;
			}

			if (!TryBindDirectValue(ref reader, kinds[i], tokenType, properties[i], typedSetters[i], instance))
				return false;
		}

		if (!reader.Read())
		{
			incomplete = true;
			return false;
		}

		// More cells than columns - the slow path raises the canonical JsonException.
		if (reader.TokenType != JsonTokenType.EndArray)
			return false;

		item = (T)instance;
		return true;
	}

	/// <summary>
	/// Deserializes one cell through its own contract and assigns it. A truncated cell reports incomplete; a cell
	/// the contract rejects returns false so the slow path re-reads the row and raises the canonical error.
	/// </summary>
	private static bool TryBindConverterCell(
		ref Utf8JsonReader reader,
		JsonTokenType tokenType,
		DirectRowBinder binder,
		int index,
		object instance,
		out bool incomplete)
	{
		incomplete = false;

		// A single-valued multi-value field arrives as a bare scalar, and a collection contract without an element
		// contract (arrays, sets, immutable collections) cannot take the wrap that would make it bind. Falling back
		// here keeps the row's outcome identical while skipping a throw that costs tens of microseconds. The cell's
		// own contract decides: a collection-typed property whose contract is a value converter, as byte[] is, binds
		// a bare scalar directly.
		if (binder.IsCollection[index]
			&& tokenType != JsonTokenType.StartArray
			&& binder.ElementTypeInfos[index] is null
			&& binder.CellTypeInfos[index]!.Kind == JsonTypeInfoKind.Enumerable)
			return false;

		// The serializer cannot tell a truncated value from an invalid one, so probe the extent first.
		var probe = reader;
		if (!probe.TrySkip())
		{
			incomplete = true;
			return false;
		}

		var cellTypeInfo = binder.CellTypeInfos[index]!;
		var elementTypeInfo = binder.ElementTypeInfos[index];
		object? value;

		try
		{
			if (elementTypeInfo is not null && tokenType != JsonTokenType.StartArray)
			{
				// ES|QL returns a single-valued multi-value field as a bare scalar; wrap it like the row path does.
				var list = (IList)cellTypeInfo.CreateObject!();
				_ = list.Add(JsonSerializer.Deserialize(ref reader, elementTypeInfo));
				value = list;
			}
			else
			{
				value = JsonSerializer.Deserialize(ref reader, cellTypeInfo);
			}
		}
		catch (JsonException)
		{
			return false;
		}

		binder.Properties[index].Set!(instance, value);
		return true;
	}

	/// <summary>
	/// Reads the single cell of a scalar row. Null cells and unexpected token shapes return false so the serializer
	/// keeps its own semantics for them (null into a non-nullable value type is its error, not ours).
	/// </summary>
	private static bool TryBindScalarDirect<T>(ref Utf8JsonReader reader, DirectBinderKind kind, out T? item, out bool incomplete)
	{
		item = default;
		incomplete = false;

		if (!reader.Read())
		{
			incomplete = true;
			return false;
		}

		var tokenType = reader.TokenType;
		if (tokenType is JsonTokenType.Null or JsonTokenType.EndArray || !TryReadScalar(ref reader, kind, tokenType, out item))
			return false;

		if (!reader.Read())
		{
			item = default;
			incomplete = true;
			return false;
		}

		if (reader.TokenType == JsonTokenType.EndArray)
			return true;

		item = default;
		return false;
	}

	// Mirrors TryBindDirectValue; keep both switches in sync when adding a new DirectBinderKind.
	private static bool TryReadScalar<T>(ref Utf8JsonReader reader, DirectBinderKind kind, JsonTokenType tokenType, out T? item)
	{
		item = default;

		switch (kind)
		{
			case DirectBinderKind.String:
				if (tokenType != JsonTokenType.String)
					return false;
				item = (T)(object)reader.GetString()!;
				return true;

			case DirectBinderKind.Bool:
				if (tokenType is not (JsonTokenType.True or JsonTokenType.False))
					return false;
				item = Lift<bool, T>(reader.GetBoolean());
				return true;

			case DirectBinderKind.Int32:
				if (tokenType != JsonTokenType.Number || !reader.TryGetInt32(out var int32Value))
					return false;
				item = Lift<int, T>(int32Value);
				return true;

			case DirectBinderKind.Int64:
				if (tokenType != JsonTokenType.Number || !reader.TryGetInt64(out var int64Value))
					return false;
				item = Lift<long, T>(int64Value);
				return true;

			case DirectBinderKind.Double:
				if (tokenType != JsonTokenType.Number || !reader.TryGetDouble(out var doubleValue))
					return false;
				item = Lift<double, T>(doubleValue);
				return true;

			case DirectBinderKind.Single:
				if (tokenType != JsonTokenType.Number || !reader.TryGetSingle(out var singleValue))
					return false;
				item = Lift<float, T>(singleValue);
				return true;

			case DirectBinderKind.Decimal:
				if (tokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var decimalValue))
					return false;
				item = Lift<decimal, T>(decimalValue);
				return true;

			case DirectBinderKind.DateTime:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTime(out var dateTimeValue))
					return false;
				item = Lift<DateTime, T>(dateTimeValue);
				return true;

			case DirectBinderKind.DateTimeOffset:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out var dateTimeOffsetValue))
					return false;
				item = Lift<DateTimeOffset, T>(dateTimeOffsetValue);
				return true;

			case DirectBinderKind.Guid:
				if (tokenType != JsonTokenType.String || !reader.TryGetGuid(out var guidValue))
					return false;
				item = Lift<Guid, T>(guidValue);
				return true;

			default:
				return false;
		}
	}

	/// <summary>Converts a parsed cell to <c>T</c>, which the classification guarantees is <c>TValue</c> or <c>TValue?</c>, without boxing.</summary>
	private static T Lift<TValue, T>(TValue value) where TValue : struct
	{
		if (typeof(T) == typeof(TValue))
			return Unsafe.As<TValue, T>(ref value);

		// TryClassifyScalar guarantees T is TValue or TValue?; anything else is a classification bug.
		Debug.Assert(typeof(T) == typeof(TValue?), $"Expected T to be {typeof(TValue)} or {typeof(TValue?)}, but got {typeof(T)}.");
		TValue? nullable = value;
		return Unsafe.As<TValue?, T>(ref nullable);
	}

	// Mirrors TryReadScalar; keep both switches in sync when adding a new DirectBinderKind.
	private static bool TryBindDirectValue(
		ref Utf8JsonReader reader,
		DirectBinderKind kind,
		JsonTokenType tokenType,
		JsonPropertyInfo property,
		Delegate? typedSetter,
		object instance)
	{
		switch (kind)
		{
			case DirectBinderKind.String:
				if (tokenType != JsonTokenType.String)
					return false;
				Assign<string?>(typedSetter, property, instance, reader.GetString());
				return true;

			case DirectBinderKind.Bool:
				if (tokenType is not (JsonTokenType.True or JsonTokenType.False))
					return false;
				Assign(typedSetter, property, instance, reader.GetBoolean());
				return true;

			case DirectBinderKind.Int32:
				if (tokenType != JsonTokenType.Number || !reader.TryGetInt32(out var int32Value))
					return false;
				Assign(typedSetter, property, instance, int32Value);
				return true;

			case DirectBinderKind.Int64:
				if (tokenType != JsonTokenType.Number || !reader.TryGetInt64(out var int64Value))
					return false;
				Assign(typedSetter, property, instance, int64Value);
				return true;

			case DirectBinderKind.Double:
				if (tokenType != JsonTokenType.Number || !reader.TryGetDouble(out var doubleValue))
					return false;
				Assign(typedSetter, property, instance, doubleValue);
				return true;

			case DirectBinderKind.Single:
				if (tokenType != JsonTokenType.Number || !reader.TryGetSingle(out var singleValue))
					return false;
				Assign(typedSetter, property, instance, singleValue);
				return true;

			case DirectBinderKind.Decimal:
				if (tokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var decimalValue))
					return false;
				Assign(typedSetter, property, instance, decimalValue);
				return true;

			case DirectBinderKind.DateTime:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTime(out var dateTimeValue))
					return false;
				Assign(typedSetter, property, instance, dateTimeValue);
				return true;

			case DirectBinderKind.DateTimeOffset:
				if (tokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out var dateTimeOffsetValue))
					return false;
				Assign(typedSetter, property, instance, dateTimeOffsetValue);
				return true;

			case DirectBinderKind.Guid:
				if (tokenType != JsonTokenType.String || !reader.TryGetGuid(out var guidValue))
					return false;
				Assign(typedSetter, property, instance, guidValue);
				return true;

			default:
				return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Assign<TValue>(Delegate? typedSetter, JsonPropertyInfo property, object instance, TValue value)
	{
		if (typedSetter is Action<object, TValue> typed)
			typed(instance, value);
		else
			property.Set!(instance, value);
	}
}
