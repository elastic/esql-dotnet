// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Generator.Analysis;
using Elastic.Mapping.Generator.Emitters;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elastic.Mapping.Generator;

/// <summary>
/// Incremental source generator that generates Elasticsearch mapping classes.
/// </summary>
[Generator]
public class MappingSourceGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// Find all partial types with [Index] or [DataStream] attributes
		var typeDeclarations = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (node, _) => IsCandidateType(node),
				transform: static (ctx, ct) => GetTypeMappingModel(ctx, ct)
			)
			.Where(static model => model != null)
			.Select(static (model, _) => model!);

		// Generate source for each type
		context.RegisterSourceOutput(typeDeclarations, static (ctx, model) => Execute(ctx, model));
	}

	private static bool IsCandidateType(SyntaxNode node)
	{
		if (node is not TypeDeclarationSyntax typeDecl)
			return false;

		// Must be partial
		if (!typeDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
			return false;

		// Must have at least one attribute
		if (typeDecl.AttributeLists.Count == 0)
			return false;

		// Quick check for potential mapping attributes
		foreach (var attrList in typeDecl.AttributeLists)
		{
			foreach (var attr in attrList.Attributes)
			{
				var name = attr.Name.ToString();
				if (name.Contains("Index") || name.Contains("DataStream"))
					return true;
			}
		}

		return false;
	}

	private static TypeMappingModel? GetTypeMappingModel(GeneratorSyntaxContext context, CancellationToken ct)
	{
		var typeDecl = (TypeDeclarationSyntax)context.Node;
		var symbol = context.SemanticModel.GetDeclaredSymbol(typeDecl, ct);

		if (symbol is not INamedTypeSymbol namedSymbol)
			return null;

		return TypeAnalyzer.Analyze(namedSymbol, ct);
	}

	private static void Execute(SourceProductionContext context, TypeMappingModel model)
	{
		// Generate the static ElasticsearchContext class
		var mappingSource = MappingContextEmitter.Emit(model);
		context.AddSource($"{model.FullTypeName}.ElasticsearchContext.g.cs", mappingSource);

		// Generate the fluent config builder
		var configSource = ConfigBuilderEmitter.Emit(model);
		context.AddSource($"{model.FullTypeName}.MappingConfig.g.cs", configSource);
	}
}
