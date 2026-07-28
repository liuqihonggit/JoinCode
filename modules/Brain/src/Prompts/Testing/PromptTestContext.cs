namespace Core.Prompts.Testing;

/// <summary>
/// 提示词测试上下文
/// </summary>
public sealed class PromptTestContext
{
    public PromptTestConfig Config { get; }

    public PromptTestContext(PromptTestConfig? config = null)
    {
        Config = config ?? new PromptTestConfig();
    }
}

/// <summary>
/// 提示词测试配置
/// </summary>
public sealed record PromptTestConfig
{
    public bool IsBriefEnabled { get; set; }
    public bool IsAgentMode { get; set; }
    public bool IsReplMode { get; set; }
    public bool HasTodoTool { get; set; }
    public bool HasTaskTool { get; set; }
    public bool EnableNumericLength { get; set; }
    public bool HasTokenBudget { get; set; }
    public bool IsGitWorktree { get; set; }
    public bool IsSimpleMode { get; set; }
    public bool ToolResultClearingEnabled { get; set; } = true;

    public string? CustomIntro { get; set; }
    public string? ProjectRules { get; set; }
    public IReadOnlyList<ExternalRuleEntry>? ExternalRules { get; set; }
    public IEnumerable<string>? McpServers { get; set; }
    public IEnumerable<string>? EnabledTools { get; set; }
    public string? ScratchpadPath { get; set; }
    public string? LanguagePreference { get; set; }
    public string? ModelId { get; set; }
    public string? ModelName { get; set; }
    public string? Version { get; set; }
    public string? BuildTime { get; set; }
    public string? IssuesExplainer { get; set; }
    public string? FeedbackChannel { get; set; }
    public IEnumerable<string>? AdditionalWorkdirs { get; set; }
    public string? AdditionalEnvInfo { get; set; }
}
