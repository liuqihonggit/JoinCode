
using JoinCode.Abstractions.Interfaces;

namespace Core.Prompts.Testing;

/// <summary>
/// 提示词触发测试器
/// </summary>
public sealed class PromptTriggerTester
{
    private readonly TriggerConditionMapper _conditionMapper = new();
    private readonly IFileSystem _fs;

    public PromptTriggerTester(IFileSystem fs)
    {
        _fs = fs;
    }

    /// <summary>
    /// 测试所有Section的触发情况
    /// </summary>
    public PromptTriggerReport TestTriggers(PromptTestContext context)
    {
        var report = new PromptTriggerReport();

        // 创建不同配置的Provider实例进行测试
        var scenarios = CreateTestScenarios(context);

        foreach (var scenario in scenarios)
        {
            var scenarioResults = TestScenario(scenario, context);
            report.AddResults(scenarioResults);
        }

        return report;
    }

    /// <summary>
    /// 测试单个场景
    /// </summary>
    private List<PromptTriggerResult> TestScenario(TestScenario scenario, PromptTestContext baseContext)
    {
        var results = new List<PromptTriggerResult>();
        var scenarioContext = new PromptTestContext(scenario.Config);

        // 创建Provider实例
        var provider = CreateProvider(scenario.Config);

        // 获取所有Section
        var sections = provider.GetSections().ToList();

        foreach (var section in sections)
        {
            var stopwatch = Stopwatch.StartNew();

            // 执行计算
            var output = section.Compute();
            var isTriggered = !string.IsNullOrEmpty(output);

            stopwatch.Stop();

            // 从Section名称推导预期触发条件
            var condition = _conditionMapper.DeriveFromSectionName(section.Name);
            var expectedTriggered = condition?.Test(scenarioContext) ?? true;

            results.Add(new PromptTriggerResult(
                sectionName: section.Name,
                scenarioName: scenario.Name,
                isTriggered: isTriggered,
                expectedTriggered: expectedTriggered,
                isCorrect: isTriggered == expectedTriggered,
                conditionDescription: condition?.Description,
                output: output,
                duration: stopwatch.Elapsed
            ));
        }

        return results;
    }

    /// <summary>
    /// 创建测试场景
    /// </summary>
    private List<TestScenario> CreateTestScenarios(PromptTestContext ctx)
    {
        var baseConfig = ctx.Config;

        return new List<TestScenario>
        {
            new("默认配置", baseConfig with { }),
            new("Agent模式", CloneConfig(baseConfig, c => c.IsAgentMode = true)),
            new("简洁模式", CloneConfig(baseConfig, c => c.IsBriefEnabled = true)),
            new("Agent+简洁", CloneConfig(baseConfig, c => { c.IsAgentMode = true; c.IsBriefEnabled = true; })),
            new("REPL模式", CloneConfig(baseConfig, c => c.IsReplMode = true)),
            new("有MCP服务器", CloneConfig(baseConfig, c => c.McpServers = new[] { "test-server" })),
            new("有Token预算", CloneConfig(baseConfig, c => c.HasTokenBudget = true)),
            new("Git工作区", CloneConfig(baseConfig, c => c.IsGitWorktree = true)),
            new("有项目规则", CloneConfig(baseConfig, c => c.ProjectRules = "测试规则")),
            new("有外部规则", CloneConfig(baseConfig, c => c.ExternalRules = [
                new ExternalRuleEntry { Name = "test-rule", Content = "测试外部规则", AlwaysApply = true }
            ])),
            new("有草稿板", CloneConfig(baseConfig, c => c.ScratchpadPath = "/tmp/scratchpad")),
            new("有额外工作目录", CloneConfig(baseConfig, c => c.AdditionalWorkdirs = new[] { "/tmp/workdir" })),
        };
    }

    /// <summary>
    /// 克隆配置并应用修改
    /// </summary>
    private static PromptTestConfig CloneConfig(PromptTestConfig source, Action<PromptTestConfig> modifier)
    {
        var clone = source with { };
        modifier(clone);
        return clone;
    }

    /// <summary>
    /// 根据测试配置创建Provider
    /// </summary>
    private DefaultSystemPromptProvider CreateProvider(PromptTestConfig config)
    {
        IBriefModeService? briefModeService = config.IsBriefEnabled ? new TestBriefModeService(true) : null;

        return new DefaultSystemPromptProvider(_fs, new SystemPromptProviderOptions
        {
            CustomIntro = config.CustomIntro,
            EnabledTools = config.EnabledTools,
            AdditionalEnvInfo = config.AdditionalEnvInfo,
            ProjectRules = config.ProjectRules,
            ExternalRules = config.ExternalRules,
            McpServers = config.McpServers,
            ScratchpadPath = config.ScratchpadPath,
            IsAgentMode = config.IsAgentMode,
            LanguagePreference = config.LanguagePreference,
            ModelId = config.ModelId,
            ModelName = config.ModelName,
            Version = config.Version,
            BuildTime = config.BuildTime,
            IssuesExplainer = config.IssuesExplainer,
            FeedbackChannel = config.FeedbackChannel,
            IsReplMode = config.IsReplMode,
            HasTodoTool = config.HasTodoTool,
            HasTaskTool = config.HasTaskTool,
            EnableNumericLength = config.EnableNumericLength,
            HasTokenBudget = config.HasTokenBudget,
            IsGitWorktree = config.IsGitWorktree,
            AdditionalWorkdirs = config.AdditionalWorkdirs
        }, briefModeService);
    }

    private sealed class TestBriefModeService : IBriefModeService
    {
        private readonly bool _enabled;

        public TestBriefModeService(bool enabled) => _enabled = enabled;

        public bool IsEnabled => _enabled;
        public DateTime? EnabledAt => _enabled ? DateTime.Now : null;
        public bool UserMsgOptIn { get => _enabled; set { } }
        public void Enable() { }
        public void Disable() { }
        public bool Toggle() => _enabled;
        public BriefModeStatus GetStatus() => _enabled ? BriefModeStatus.Enabled(DateTime.Now) : BriefModeStatus.Disabled();
    }
}
