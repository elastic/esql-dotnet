// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;

namespace Elastic.Mapping.Generator.Analysis;

/// <summary>
/// Analyzes type symbols to extract mapping information.
/// </summary>
internal static class TypeAnalyzer
{
	private const string IndexAttributeName = "Elastic.Mapping.IndexAttribute";
	private const string DataStreamAttributeName = "Elastic.Mapping.DataStreamAttribute";
	private const string JsonPropertyNameAttributeName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
	private const string JsonIgnoreAttributeName = "System.Text.Json.Serialization.JsonIgnoreAttribute";

	// Field type attribute names
	private const string TextAttributeName = "Elastic.Mapping.TextAttribute";
	private const string KeywordAttributeName = "Elastic.Mapping.KeywordAttribute";
	private const string DateAttributeName = "Elastic.Mapping.DateAttribute";
	private const string LongAttributeName = "Elastic.Mapping.LongAttribute";
	private const string DoubleAttributeName = "Elastic.Mapping.DoubleAttribute";
	private const string BooleanAttributeName = "Elastic.Mapping.BooleanAttribute";
	private const string NestedAttributeName = "Elastic.Mapping.NestedAttribute";
	private const string ObjectAttributeName = "Elastic.Mapping.ObjectAttribute";
	private const string IpAttributeName = "Elastic.Mapping.IpAttribute";
	private const string GeoPointAttributeName = "Elastic.Mapping.GeoPointAttribute";
	private const string GeoShapeAttributeName = "Elastic.Mapping.GeoShapeAttribute";
	private const string CompletionAttributeName = "Elastic.Mapping.CompletionAttribute";
	private const string DenseVectorAttributeName = "Elastic.Mapping.DenseVectorAttribute";
	private const string SemanticTextAttributeName = "Elastic.Mapping.SemanticTextAttribute";

	public static TypeMappingModel? Analyze(INamedTypeSymbol typeSymbol, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		var indexConfig = GetIndexConfig(typeSymbol);
		var dataStreamConfig = GetDataStreamConfig(typeSymbol);

		// Must have either [Index] or [DataStream]
		if (indexConfig == null && dataStreamConfig == null)
			return null;

		var isPartial = typeSymbol.DeclaringSyntaxReferences
			.Any(r => r.GetSyntax(ct) is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax tds &&
					  tds.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

		if (!isPartial)
			return null;

		var properties = GetProperties(typeSymbol, ct);
		var containingTypes = GetContainingTypes(typeSymbol);
		var analysisComponents = ConfigureAnalysisParser.Parse(typeSymbol, ct);

		// Detect Configure* methods
		var (hasConfigureAnalysis, hasConfigureMappings, mappingsBuilderTypeName) = DetectConfigureMethods(typeSymbol);

		return new TypeMappingModel(
			typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
			typeSymbol.Name,
			isPartial,
			indexConfig,
			dataStreamConfig,
			properties,
			containingTypes,
			analysisComponents,
			hasConfigureAnalysis,
			hasConfigureMappings,
			mappingsBuilderTypeName
		);
	}

	private static IndexConfigModel? GetIndexConfig(INamedTypeSymbol typeSymbol)
	{
		var attr = typeSymbol.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == IndexAttributeName);

		if (attr == null)
			return null;

		return new IndexConfigModel(
			GetNamedArg<string>(attr, "Name"),
			GetNamedArg<string>(attr, "WriteAlias"),
			GetNamedArg<string>(attr, "ReadAlias"),
			GetNamedArg<string>(attr, "DatePattern"),
			GetNamedArg<string>(attr, "SearchPattern"),
			GetNamedArg<int>(attr, "Shards", -1),
			GetNamedArg<int>(attr, "Replicas", -1),
			GetNamedArg<string>(attr, "RefreshInterval"),
			GetNamedArg<bool>(attr, "Dynamic", true)
		);
	}

	private static DataStreamConfigModel? GetDataStreamConfig(INamedTypeSymbol typeSymbol)
	{
		var attr = typeSymbol.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DataStreamAttributeName);

		if (attr == null)
			return null;

		var type = GetNamedArg<string>(attr, "Type");
		var dataset = GetNamedArg<string>(attr, "Dataset");

		if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(dataset))
			return null;

		return new DataStreamConfigModel(
			type!,
			dataset!,
			GetNamedArg<string>(attr, "Namespace") ?? "default"
		);
	}

	private static ImmutableArray<PropertyMappingModel> GetProperties(INamedTypeSymbol typeSymbol, CancellationToken ct) =>
		GetProperties(typeSymbol, [], ct);

	private static ImmutableArray<PropertyMappingModel> GetProperties(
		INamedTypeSymbol typeSymbol,
		HashSet<string> visitedTypes,
		CancellationToken ct
	)
	{
		var builder = ImmutableArray.CreateBuilder<PropertyMappingModel>();

		foreach (var member in typeSymbol.GetMembers())
		{
			ct.ThrowIfCancellationRequested();

			if (member is not IPropertySymbol property)
				continue;

			if (property.DeclaredAccessibility != Accessibility.Public)
				continue;

			if (property.IsStatic || property.IsIndexer)
				continue;

			var propModel = AnalyzeProperty(property, visitedTypes, ct);
			if (propModel != null)
				builder.Add(propModel);
		}

		return builder.ToImmutable();
	}

	private static PropertyMappingModel? AnalyzeProperty(IPropertySymbol property) =>
		AnalyzeProperty(property, [], CancellationToken.None);

	private static PropertyMappingModel? AnalyzeProperty(
		IPropertySymbol property,
		HashSet<string> visitedTypes,
		CancellationToken ct
	)
	{
		var attrs = property.GetAttributes();

		// Check for [JsonIgnore]
		var isIgnored = attrs.Any(a => a.AttributeClass?.ToDisplayString() == JsonIgnoreAttributeName);

		// Get field name from [JsonPropertyName] or use camelCase
		var fieldName = attrs
			.Where(a => a.AttributeClass?.ToDisplayString() == JsonPropertyNameAttributeName)
			.Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
			.FirstOrDefault() ?? ToCamelCase(property.Name);

		// Determine field type and options from attributes or CLR type
		var (fieldType, options) = DetermineFieldTypeAndOptions(property, attrs);

		// For nested/object types, recursively analyze the element type
		NestedTypeModel? nestedType = null;
		if (fieldType is FieldTypes.Nested or FieldTypes.Object)
			nestedType = AnalyzeNestedType(property.Type, visitedTypes, ct);

		return PropertyMappingModel.Create(
			property.Name,
			fieldName,
			fieldType,
			isIgnored,
			options,
			nestedType
		);
	}

	private static NestedTypeModel? AnalyzeNestedType(
		ITypeSymbol typeSymbol,
		HashSet<string> visitedTypes,
		CancellationToken ct
	)
	{
		// Get the element type (unwrap List<T>, T[], etc.)
		var elementType = GetElementType(typeSymbol);
		if (elementType is not INamedTypeSymbol namedType)
			return null;

		var fullyQualifiedName = namedType.ToDisplayString();

		// Prevent circular references
		if (!visitedTypes.Add(fullyQualifiedName))
			return null;

		try
		{
			var properties = GetNestedTypeProperties(namedType, visitedTypes, ct);

			if (properties.Length == 0)
				return null;

			return new NestedTypeModel(namedType.Name, fullyQualifiedName, properties);
		}
		finally
		{
			visitedTypes.Remove(fullyQualifiedName);
		}
	}

	private static ITypeSymbol GetElementType(ITypeSymbol typeSymbol)
	{
		// Handle nullable
		if (typeSymbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
			typeSymbol = nullable.TypeArguments[0];

		// Handle arrays
		if (typeSymbol is IArrayTypeSymbol arrayType)
			return arrayType.ElementType;

		// Handle generic collections (List<T>, IEnumerable<T>, etc.)
		if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
		{
			var originalDef = namedType.OriginalDefinition.ToDisplayString();
			if (originalDef.StartsWith("System.Collections.Generic.", StringComparison.Ordinal) ||
				originalDef == "System.Collections.IEnumerable")
			{
				if (namedType.TypeArguments.Length > 0)
					return namedType.TypeArguments[0];
			}
		}

		return typeSymbol;
	}

	private static ImmutableArray<PropertyMappingModel> GetNestedTypeProperties(
		INamedTypeSymbol typeSymbol,
		HashSet<string> visitedTypes,
		CancellationToken ct
	)
	{
		var builder = ImmutableArray.CreateBuilder<PropertyMappingModel>();

		foreach (var member in typeSymbol.GetMembers())
		{
			ct.ThrowIfCancellationRequested();

			if (member is not IPropertySymbol property)
				continue;

			if (property.DeclaredAccessibility != Accessibility.Public)
				continue;

			if (property.IsStatic || property.IsIndexer)
				continue;

			var propModel = AnalyzeProperty(property, visitedTypes, ct);
			if (propModel != null)
				builder.Add(propModel);
		}

		return builder.ToImmutable();
	}

	private static (string FieldType, ImmutableDictionary<string, string?> Options) DetermineFieldTypeAndOptions(
		IPropertySymbol property,
		ImmutableArray<AttributeData> attrs)
	{
		var optionsBuilder = ImmutableDictionary.CreateBuilder<string, string?>();

		// Check for explicit type attributes first
		foreach (var attr in attrs)
		{
			var attrName = attr.AttributeClass?.ToDisplayString();

			switch (attrName)
			{
				case TextAttributeName:
					ExtractOptions(attr, optionsBuilder, "Analyzer", "SearchAnalyzer", "Norms", "Index");
					return (FieldTypes.Text, optionsBuilder.ToImmutable());

				case KeywordAttributeName:
					ExtractOptions(attr, optionsBuilder, "Normalizer", "IgnoreAbove", "DocValues", "Index");
					return (FieldTypes.Keyword, optionsBuilder.ToImmutable());

				case DateAttributeName:
					ExtractOptions(attr, optionsBuilder, "Format", "DocValues", "Index");
					return (FieldTypes.Date, optionsBuilder.ToImmutable());

				case LongAttributeName:
					ExtractOptions(attr, optionsBuilder, "DocValues", "Index");
					return (FieldTypes.Long, optionsBuilder.ToImmutable());

				case DoubleAttributeName:
					ExtractOptions(attr, optionsBuilder, "DocValues", "Index");
					return (FieldTypes.Double, optionsBuilder.ToImmutable());

				case BooleanAttributeName:
					ExtractOptions(attr, optionsBuilder, "DocValues", "Index");
					return (FieldTypes.Boolean, optionsBuilder.ToImmutable());

				case NestedAttributeName:
					ExtractOptions(attr, optionsBuilder, "IncludeInParent", "IncludeInRoot");
					return (FieldTypes.Nested, optionsBuilder.ToImmutable());

				case ObjectAttributeName:
					ExtractOptions(attr, optionsBuilder, "Enabled");
					return (FieldTypes.Object, optionsBuilder.ToImmutable());

				case IpAttributeName:
					return (FieldTypes.Ip, optionsBuilder.ToImmutable());

				case GeoPointAttributeName:
					return (FieldTypes.GeoPoint, optionsBuilder.ToImmutable());

				case GeoShapeAttributeName:
					return (FieldTypes.GeoShape, optionsBuilder.ToImmutable());

				case CompletionAttributeName:
					ExtractOptions(attr, optionsBuilder, "Analyzer", "SearchAnalyzer");
					return (FieldTypes.Completion, optionsBuilder.ToImmutable());

				case DenseVectorAttributeName:
					ExtractOptions(attr, optionsBuilder, "Dims", "Similarity");
					return (FieldTypes.DenseVector, optionsBuilder.ToImmutable());

				case SemanticTextAttributeName:
					ExtractOptions(attr, optionsBuilder, "InferenceId");
					return (FieldTypes.SemanticText, optionsBuilder.ToImmutable());
			}
		}

		// Auto-infer from CLR type
		var fieldType = InferFieldType(property.Type);
		return (fieldType, optionsBuilder.ToImmutable());
	}

	private static void ExtractOptions(
		AttributeData attr,
		ImmutableDictionary<string, string?>.Builder builder,
		params string[] optionNames)
	{
		foreach (var optionName in optionNames)
		{
			var value = GetNamedArgRaw(attr, optionName);
			if (value != null)
				builder[ToCamelCase(optionName)] = FormatValue(value);
		}
	}

	private static string InferFieldType(ITypeSymbol type)
	{
		// Unwrap nullable
		if (type is INamedTypeSymbol namedType &&
			namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
			type = namedType.TypeArguments[0];

		var typeName = type.ToDisplayString();

		return typeName switch
		{
			"string" => FieldTypes.Keyword,
			"int" or "System.Int32" => FieldTypes.Integer,
			"long" or "System.Int64" => FieldTypes.Long,
			"short" or "System.Int16" => FieldTypes.Short,
			"byte" or "System.Byte" => FieldTypes.Byte,
			"double" or "System.Double" => FieldTypes.Double,
			"float" or "System.Single" => FieldTypes.Float,
			"decimal" or "System.Decimal" => FieldTypes.Double,
			"bool" or "System.Boolean" => FieldTypes.Boolean,
			"System.DateTime" or "System.DateTimeOffset" => FieldTypes.Date,
			"System.Guid" => FieldTypes.Keyword,
			_ when type.TypeKind == TypeKind.Enum => FieldTypes.Keyword,
			_ => FieldTypes.Object
		};
	}

	private static T? GetNamedArg<T>(AttributeData attr, string name, T? defaultValue = default)
	{
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		if (arg.Key == null)
			return defaultValue;

		return arg.Value.Value is T value ? value : defaultValue;
	}

	private static object? GetNamedArgRaw(AttributeData attr, string name)
	{
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		return arg.Key != null ? arg.Value.Value : null;
	}

	private static string FormatValue(object value) =>
		value switch
		{
			bool b => b ? "true" : "false",
			string s => $"\"{s}\"",
			_ => value.ToString() ?? string.Empty
		};

	private static ImmutableArray<string> GetContainingTypes(INamedTypeSymbol typeSymbol)
	{
		var builder = ImmutableArray.CreateBuilder<string>();
		var containing = typeSymbol.ContainingType;

		while (containing != null)
		{
			builder.Insert(0, containing.Name);
			containing = containing.ContainingType;
		}

		return builder.ToImmutable();
	}

	private static string ToCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		if (name.Length == 1)
			return name.ToLowerInvariant();

		return char.ToLowerInvariant(name[0]) + name.Substring(1);
	}

	private static (bool HasConfigureAnalysis, bool HasConfigureMappings, string? MappingsBuilderTypeName) DetectConfigureMethods(INamedTypeSymbol typeSymbol)
	{
		var hasConfigureAnalysis = typeSymbol.GetMembers("ConfigureAnalysis")
			.OfType<IMethodSymbol>()
			.Any(m => m.IsStatic && m.Parameters.Length == 1);

		var configureMappingsMethod = typeSymbol.GetMembers("ConfigureMappings")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		var hasConfigureMappings = configureMappingsMethod != null;
		string? mappingsBuilderTypeName = null;

		if (hasConfigureMappings && configureMappingsMethod != null)
		{
			// Extract the builder type name from the parameter type
			var parameterType = configureMappingsMethod.Parameters[0].Type;
			mappingsBuilderTypeName = parameterType.Name;
		}

		return (hasConfigureAnalysis, hasConfigureMappings, mappingsBuilderTypeName);
	}
}
