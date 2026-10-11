namespace Core.Tests.DependencyInjection;

/// <summary>
/// SyncSystemPromptProviderOptions 确定性测试 — ConvertExternalRules 纯计算 + 构造函数条件转换。
/// </summary>
public sealed class SyncSystemPromptProviderOptionsTests {
    private readonly Mock<IFileSystem> _fs;

    public SyncSystemPromptProviderOptionsTests() {
        _fs = new Mock<IFileSystem>();
    }

    // ===== ConvertExternalRules 纯计算 =====

    [Fact]
    public void ConvertExternalRules_EmptyList_ReturnsEmptyArray() {
        var result = SyncSystemPromptProviderOptions.ConvertExternalRules([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertExternalRules_SingleRule_MapsAllFields() {
        var rules = new List<RuleFile> {
            new() { Name = "test-rule", Content = "rule content", SourcePath = "/path", AlwaysApply = true, Globs = "*.cs", Description = "desc" }
        };
        var result = SyncSystemPromptProviderOptions.ConvertExternalRules(rules);
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("test-rule");
        result[0].Content.Should().Be("rule content");
        result[0].SourcePath.Should().Be("/path");
        result[0].AlwaysApply.Should().BeTrue();
        result[0].Globs.Should().Be("*.cs");
        result[0].Description.Should().Be("desc");
    }

    [Fact]
    public void ConvertExternalRules_MultipleRules_PreservesOrder() {
        var rules = new List<RuleFile> {
            new() { Name = "first", Content = "c1" },
            new() { Name = "second", Content = "c2" },
            new() { Name = "third", Content = "c3" }
        };
        var result = SyncSystemPromptProviderOptions.ConvertExternalRules(rules);
        result.Should().HaveCount(3);
        result[0].Name.Should().Be("first");
        result[1].Name.Should().Be("second");
        result[2].Name.Should().Be("third");
    }

    [Fact]
    public void ConvertExternalRules_DefaultOptionalFields_MapsToDefaults() {
        var rules = new List<RuleFile> {
            new() { Name = "minimal", Content = "content" }
        };
        var result = SyncSystemPromptProviderOptions.ConvertExternalRules(rules);
        result[0].SourcePath.Should().BeEmpty();
        result[0].AlwaysApply.Should().BeFalse();
        result[0].Globs.Should().BeEmpty();
        result[0].Description.Should().BeEmpty();
    }

    // ===== 构造函数条件转换 =====

    [Fact]
    public void Ctor_EmptyExternalRules_SetsEmptyExternalRules() {
        var config = new WorkflowConfig { ExternalRules = [] };
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object);
        sut.ExternalRules.Should().BeEmpty();
    }

    [Fact]
    public void Ctor_NonEmptyExternalRules_ConvertsToExternalRuleEntries() {
        var config = new WorkflowConfig {
            ExternalRules = new List<RuleFile> {
                new() { Name = "rule1", Content = "content1", AlwaysApply = true }
            }
        };
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object);
        sut.ExternalRules.Should().HaveCount(1);
        sut.ExternalRules[0].Name.Should().Be("rule1");
        sut.ExternalRules[0].Content.Should().Be("content1");
        sut.ExternalRules[0].AlwaysApply.Should().BeTrue();
    }

    [Fact]
    public void Ctor_ProjectRules_MapsFromConfig() {
        var config = new WorkflowConfig { ProjectRules = "custom project rules" };
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object);
        sut.ProjectRules.Should().Be("custom project rules");
    }

    [Fact]
    public void Ctor_NullDailyLogService_DailyLogPromptBuilderIsNull() {
        var config = new WorkflowConfig();
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object, dailyLogService: null);
        sut.DailyLogPromptBuilder.Should().BeNull();
    }

    [Fact]
    public void Ctor_NonNullDailyLogService_DailyLogPromptBuilderIsNotNull() {
        var config = new WorkflowConfig();
        var fileContext = new FileContextTracker();
        var dailyLogService = new Mock<IAssistantDailyLogService>();
        dailyLogService.Setup(d => d.BuildDailyLogPromptAsync()).ReturnsAsync("daily log");
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object, dailyLogService: dailyLogService.Object);
        sut.DailyLogPromptBuilder.Should().NotBeNull();
    }

    [Fact]
    public void Ctor_NullSearchHistoryService_SearchHistoryPromptBuilderIsNull() {
        var config = new WorkflowConfig();
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object, searchHistoryService: null);
        sut.SearchHistoryPromptBuilder.Should().BeNull();
    }

    [Fact]
    public void Ctor_NonNullSearchHistoryService_SearchHistoryPromptBuilderIsNotNull() {
        var config = new WorkflowConfig();
        var fileContext = new FileContextTracker();
        var searchHistoryService = new Mock<IMemorySearchHistoryService>();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object, searchHistoryService: searchHistoryService.Object);
        sut.SearchHistoryPromptBuilder.Should().NotBeNull();
    }

    [Fact]
    public void Ctor_NullActuatorRegistry_ShellInfosIsEmptyDictionary() {
        var config = new WorkflowConfig();
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object, actuatorRegistry: null);
        sut.ShellInfos.Should().BeEmpty();
    }

    [Fact]
    public void Ctor_HasTeamToolsAndHasSendMessage_AlwaysTrue() {
        var config = new WorkflowConfig();
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object);
        sut.HasTeamTools.Should().BeTrue();
        sut.HasSendMessage.Should().BeTrue();
    }

    [Fact]
    public void Ctor_AgentDefinitions_IsEmpty() {
        var config = new WorkflowConfig();
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object);
        sut.AgentDefinitions.Should().BeEmpty();
    }

    [Fact]
    public void Ctor_AwaySummary_IsNull() {
        var config = new WorkflowConfig();
        var fileContext = new FileContextTracker();
        var sut = new SyncSystemPromptProviderOptions(config, fileContext, _fs.Object);
        sut.AwaySummary.Should().BeNull();
    }
}
