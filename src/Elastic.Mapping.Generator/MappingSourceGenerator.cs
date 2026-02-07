// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using Elastic.Mapping.Generator.Analysis;
using Elastic.Mapping.Generator.Emitters;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elastic.Mapping.Generator;

/// <summary>
/// Incremental source generator that generates Elasticsearch mapping resolver classes
/// from context classes annotated with <c>[ElasticsearchMappingContext]</c>.
/// </summary>
[Generator]
public class MappingSourceGenerator : IIncrementalGenerator
{
	private const string ElasticsearchMappingContextAttributeName = "Elastic.Mapping.ElasticsearchMappingContextAttribute";
	private const string IndexAttributePrefix = "Elastic.Mapping.IndexAttribute<";
	private const string DataStreamAttributePrefix = "Elastic.Mapping.DataStreamAttribute<";

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var contextDeclarations = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (node, _) => IsCandidateContextClass(node),
				transform: static (ctx, ct) => GetContextModel(ctx, ct)
			)
			.Where(static model => model != null)
			.Select(static (model, _) => model!);

		context.RegisterSourceOutput(contextDeclarations, static (ctx, model) => ExecuteContext(ctx, model));
	}

	private static bool IsCandidateContextClass(SyntaxNode node)
	{
		if (node is not TypeDeclarationSyntax typeDecl)
			return false;

		// Must be partial
		if (!typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
			return false;

		// Must have at least one attribute
		if (typeDecl.AttributeLists.Count == 0)
			return false;

		// Quick check for the mapping context attribute
		foreach (var attrList in typeDecl.AttributeLists)
		{
			foreach (var attr in attrList.Attributes)
			{
				var name = attr.Name.ToString();
				if (name.Contains("ElasticsearchMappingContext"))
					return true;
			}
		}

		return false;
	}

	private static ContextMappingModel? GetContextModel(GeneratorSyntaxContext context, CancellationToken ct)
	{
		var typeDecl = (TypeDeclarationSyntax)context.Node;
		var symbol = context.SemanticModel.GetDeclaredSymbol(typeDecl, ct);

		if (symbol is not INamedTypeSymbol contextSymbol)
			return null;

		// Verify it has [ElasticsearchMappingContext]
		var contextAttr = contextSymbol.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ElasticsearchMappingContextAttributeName);

		if (contextAttr == null)
			return null;

		// Extract JsonContext type from the attribute
		INamedTypeSymbol? jsonContextSymbol = null;
		var jsonContextArg = contextAttr.NamedArguments
			.FirstOrDefault(a => a.Key == "JsonContext");
		if (jsonContextArg.Key != null && jsonContextArg.Value.Value is INamedTypeSymbol jcs)
			jsonContextSymbol = jcs;

		// Analyze STJ context if provided
		var stjConfig = StjContextAnalyzer.Analyze(jsonContextSymbol);

		// Collect type registrations from [Index<T>] and [DataStream<T>] attributes
		var registrations = ImmutableArray.CreateBuilder<TypeRegistration>();

		foreach (var attr in contextSymbol.GetAttributes())
		{
			ct.ThrowIfCancellationRequested();

			var attrClassName = attr.AttributeClass?.ToDisplayString();
			if (attrClassName == null)
				continue;

			if (attrClassName.StartsWith(IndexAttributePrefix, StringComparison.Ordinal))
			{
				var registration = ProcessIndexAttribute(attr, contextSymbol, stjConfig, ct);
				if (registration != null)
					registrations.Add(registration);
			}
			else if (attrClassName.StartsWith(DataStreamAttributePrefix, StringComparison.Ordinal))
			{
				var registration = ProcessDataStreamAttribute(attr, contextSymbol, stjConfig, ct);
				if (registration != null)
					registrations.Add(registration);
			}
		}

		if (registrations.Count == 0)
			return null;

		return new ContextMappingModel(
			contextSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
			contextSymbol.Name,
			stjConfig,
			registrations.ToImmutable()
		);
	}

	private static TypeRegistration? ProcessIndexAttribute(
		AttributeData attr,
		INamedTypeSymbol contextSymbol,
		StjContextConfig? stjConfig,
		CancellationToken ct)
	{
		// Extract T from IndexAttribute<T>
		var typeArg = attr.AttributeClass?.TypeArguments.FirstOrDefault();
		if (typeArg is not INamedTypeSymbol targetType)
			return null;

		var indexConfig = new IndexConfigModel(
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

		// Extract Configuration class reference (keep the symbol for method detection)
		string? configClassName = null;
		INamedTypeSymbol? configTypeSymbol = null;
		var configArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Configuration");
		if (configArg.Key != null && configArg.Value.Value is INamedTypeSymbol configType)
		{
			configClassName = configType.ToDisplayString();
			configTypeSymbol = configType;
		}

		return BuildTypeRegistration(targetType, contextSymbol, stjConfig, indexConfig, null, configClassName, configTypeSymbol, ct);
	}

	private static TypeRegistration? ProcessDataStreamAttribute(
		AttributeData attr,
		INamedTypeSymbol contextSymbol,
		StjContextConfig? stjConfig,
		CancellationToken ct)
	{
		// Extract T from DataStreamAttribute<T>
		var typeArg = attr.AttributeClass?.TypeArguments.FirstOrDefault();
		if (typeArg is not INamedTypeSymbol targetType)
			return null;

		var type = GetNamedArg<string>(attr, "Type");
		var dataset = GetNamedArg<string>(attr, "Dataset");

		if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(dataset))
			return null;

		var dataStreamConfig = new DataStreamConfigModel(
			type!,
			dataset!,
			GetNamedArg<string>(attr, "Namespace") ?? "default"
		);

		// Extract Configuration class reference (keep the symbol for method detection)
		string? configClassName = null;
		INamedTypeSymbol? configTypeSymbol = null;
		var configArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Configuration");
		if (configArg.Key != null && configArg.Value.Value is INamedTypeSymbol configType)
		{
			configClassName = configType.ToDisplayString();
			configTypeSymbol = configType;
		}

		return BuildTypeRegistration(targetType, contextSymbol, stjConfig, null, dataStreamConfig, configClassName, configTypeSymbol, ct);
	}

	private static TypeRegistration? BuildTypeRegistration(
		INamedTypeSymbol targetType,
		INamedTypeSymbol contextSymbol,
		StjContextConfig? stjConfig,
		IndexConfigModel? indexConfig,
		DataStreamConfigModel? dataStreamConfig,
		string? configClassName,
		INamedTypeSymbol? configTypeSymbol,
		CancellationToken ct)
	{
		var typeModel = TypeAnalyzer.Analyze(targetType, stjConfig, indexConfig, dataStreamConfig, ct);
		if (typeModel == null)
			return null;

		// Check configuration class for methods (priority 2)
		IMethodSymbol? configClassAnalysis = null;
		IMethodSymbol? configClassMappings = null;
		if (configTypeSymbol != null)
		{
			configClassAnalysis = configTypeSymbol.GetMembers("ConfigureAnalysis")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

			configClassMappings = configTypeSymbol.GetMembers("ConfigureMappings")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);
		}

		// Check for Configure{TypeName}Analysis/Configure{TypeName}Mappings on the context class (priority 1)
		var contextConfigureAnalysis = contextSymbol
			.GetMembers($"Configure{targetType.Name}Analysis")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		var contextConfigureMappings = contextSymbol
			.GetMembers($"Configure{targetType.Name}Mappings")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		// Check for ConfigureAnalysis/ConfigureMappings on the target type itself (priority 3 / fallback)
		var hasConfigureAnalysisOnType = typeModel.HasConfigureAnalysis;
		var hasConfigureMappingsOnType = typeModel.HasConfigureMappings;

		// Build ConfigureAnalysis reference and parse analysis components
		// Priority: context > configuration class > type
		string? configureAnalysisRef = null;
		AnalysisComponentsModel analysisComponents;

		if (contextConfigureAnalysis != null)
		{
			configureAnalysisRef = $"global::{contextSymbol.ToDisplayString()}.Configure{targetType.Name}Analysis";
			analysisComponents = ConfigureAnalysisParser.ParseFromMethod(contextConfigureAnalysis, ct);
		}
		else if (configClassAnalysis != null)
		{
			configureAnalysisRef = $"global::{configTypeSymbol!.ToDisplayString()}.ConfigureAnalysis";
			analysisComponents = ConfigureAnalysisParser.ParseFromMethod(configClassAnalysis, ct);
		}
		else if (hasConfigureAnalysisOnType)
		{
			configureAnalysisRef = $"global::{targetType.ToDisplayString()}.ConfigureAnalysis";
			analysisComponents = typeModel.AnalysisComponents;
		}
		else
		{
			analysisComponents = AnalysisComponentsModel.Empty;
		}

		// Determine if any source has ConfigureMappings
		var hasConfigureMappings = contextConfigureMappings != null
			|| configClassMappings != null
			|| hasConfigureMappingsOnType;

		return new TypeRegistration(
			targetType.Name,
			targetType.ToDisplayString(),
			typeModel,
			indexConfig,
			dataStreamConfig,
			configClassName,
			configureAnalysisRef,
			hasConfigureMappings,
			analysisComponents
		);
	}

	private static void ExecuteContext(SourceProductionContext context, ContextMappingModel model)
	{
		// Generate the context class with nested resolvers
		var contextSource = ContextEmitter.Emit(model);
		context.AddSource($"{model.ContextTypeName}.g.cs", contextSource);

		// Generate per-type MappingsBuilder classes
		foreach (var reg in model.TypeRegistrations)
		{
			var mappingsBuilderSource = MappingsBuilderEmitter.EmitForContext(model, reg);
			context.AddSource($"{model.ContextTypeName}.{reg.TypeName}MappingsBuilder.g.cs", mappingsBuilderSource);

			// Generate analysis names if there are analysis components
			var analysisNamesSource = AnalysisNamesEmitter.EmitForContext(model, reg);
			if (analysisNamesSource != null)
				context.AddSource($"{model.ContextTypeName}.{reg.TypeName}Analysis.g.cs", analysisNamesSource);
		}
	}

	private static T? GetNamedArg<T>(AttributeData attr, string name, T? defaultValue = default)
	{
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		if (arg.Key == null)
			return defaultValue;

		return arg.Value.Value is T value ? value : defaultValue;
	}
}
