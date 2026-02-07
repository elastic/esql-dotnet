// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;

namespace Elastic.Mapping.Tests;

public class ConfigurationInterfaceTests
{
	[Test]
	public void Context_ProvidesElasticsearchTypeContext()
	{
		var context = TestMappingContext.LogEntry.Context;
		context.Should().NotBeNull();
		context.Hash.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void ConfigureAnalysis_ViaDelegate_Works()
	{
		var context = TestMappingContext.LogEntry.Context;
		context.ConfigureAnalysis.Should().NotBeNull();

		var builder = context.ConfigureAnalysis!(new AnalysisBuilder());
		builder.Should().NotBeNull();
		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void ConfigureAnalysis_BuildsAndMergesIntoSettings()
	{
		var context = TestMappingContext.LogEntry.Context;
		var analysis = context.ConfigureAnalysis!(new AnalysisBuilder()).Build();
		var baseSettings = TestMappingContext.LogEntry.GetSettingsJson();
		var mergedSettings = analysis.MergeIntoSettings(baseSettings);

		mergedSettings.Should().Contain("analysis");
		mergedSettings.Should().Contain("log_message_analyzer");
	}

	[Test]
	public void ConfigureAnalysis_ExplicitCall_Works()
	{
		var builder = TestMappingContext.ConfigureLogEntryAnalysis(new AnalysisBuilder());
		builder.Should().NotBeNull();
		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void SimpleDocument_WithoutConfigureMethods_HasNullDelegate()
	{
		var context = TestMappingContext.SimpleDocument.Context;
		context.ConfigureAnalysis.Should().BeNull();
	}
}
