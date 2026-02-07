// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;

namespace Elastic.Mapping.Tests;

public class ConfigurationInterfaceTests
{
	[Test]
	public void Generated_ImplementsIHasElasticsearchContext()
	{
		// Verify the generated code implements the base interface
		typeof(IHasElasticsearchContext).IsAssignableFrom(typeof(LogEntry)).Should().BeTrue();
	}

	[Test]
	public void Generated_ImplementsIHasAnalysisConfiguration()
	{
		// Verify the generated code implements the analysis interface
		typeof(IHasAnalysisConfiguration).IsAssignableFrom(typeof(LogEntry)).Should().BeTrue();
	}

	[Test]
	public void ConfigureAnalysis_ViaInterface_Works()
	{
		// Invoke via static abstract interface
		var builder = InvokeConfigureAnalysisViaInterface<LogEntry>();
		builder.Should().NotBeNull();
		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void Context_ViaInterface_Works()
	{
		// Access context via static abstract interface
		var context = GetContextViaInterface<LogEntry>();
		context.Should().NotBeNull();
		context.Hash.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void ConfigureAnalysis_BuildsAndMergesIntoSettings()
	{
		// Simulate what the ingest strategy does
		var analysis = LogEntry.ConfigureAnalysis(new AnalysisBuilder()).Build();
		var baseSettings = LogEntry.ElasticsearchContext.GetSettingsJson();
		var mergedSettings = analysis.MergeIntoSettings(baseSettings);

		mergedSettings.Should().Contain("analysis");
		mergedSettings.Should().Contain("log_message_analyzer");
	}

	[Test]
	public void ConfigureAnalysis_ExplicitCall_Works()
	{
		var builder = LogEntry.ConfigureAnalysis(new AnalysisBuilder());
		builder.Should().NotBeNull();
		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void SimpleDocument_WithoutConfigureMethods_OnlyImplementsBase()
	{
		// SimpleDocument doesn't have Configure* methods, so only base interface
		typeof(IHasElasticsearchContext).IsAssignableFrom(typeof(SimpleDocument)).Should().BeTrue();
		typeof(IHasAnalysisConfiguration).IsAssignableFrom(typeof(SimpleDocument)).Should().BeFalse();
	}

	[Test]
	public void Context_ConfigureAnalysisDelegate_PopulatedForLogEntry()
	{
		// Verify the delegate is populated in the context for types with ConfigureAnalysis
		var context = LogEntry.Context;
		context.ConfigureAnalysis.Should().NotBeNull();

		// Verify invoking the delegate works
		var builder = context.ConfigureAnalysis!(new AnalysisBuilder());
		builder.Should().NotBeNull();
		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void Context_ConfigureAnalysisDelegate_NullForSimpleDocument()
	{
		// Verify the delegate is null for types without ConfigureAnalysis
		var context = SimpleDocument.Context;
		context.ConfigureAnalysis.Should().BeNull();
	}

	private static AnalysisBuilder InvokeConfigureAnalysisViaInterface<T>() where T : IHasAnalysisConfiguration =>
		T.ConfigureAnalysis(new AnalysisBuilder());

	private static ElasticsearchTypeContext GetContextViaInterface<T>() where T : IHasElasticsearchContext =>
		T.Context;
}
