namespace Core.Prompts.Testing;

/// <summary>
/// 提示词测试上下文
/// </summary>
public sealed class PromptTestContext {
    /// <summary>
    /// 获取测试配置。
    /// </summary>
    public PromptTestConfig Config { get; }

    /// <summary>
    /// 初始化 <see cref="PromptTestContext"/> 的新实例。
    /// </summary>
    /// <param name="config">测试配置，为 null 时使用默认配置。</param>
    public PromptTestContext(PromptTestConfig? config = null) {
        Config = config ?? new PromptTestConfig();
    }
}

/// <summary>
/// 提示词测试配置
/// </summary>
public sealed record PromptTestConfig {
    /// <summary>
    /// 获取或设置是否启用简洁模式。
    /// </summary>
    public bool IsBriefEnabled { get; set; }

    /// <summary>
    /// 获取或设置是否启用 Agent 模式。
    /// </summary>
    public bool IsAgentMode { get; set; }

    /// <summary>
    /// 获取或设置是否启用 REPL 模式。
    /// </summary>
    public bool IsReplMode { get; set; }

    /// <summary>
    /// 获取或设置是否拥有 Todo 工具。
    /// </summary>
    public bool HasTodoTool { get; set; }

    /// <summary>
    /// 获取或设置是否拥有 Task 工具。
    /// </summary>
    public bool HasTaskTool { get; set; }

    /// <summary>
    /// 获取或设置是否启用数字长度。
    /// </summary>
    public bool EnableNumericLength { get; set; }

    /// <summary>
    /// 获取或设置是否拥有 Token 预算。
    /// </summary>
    public bool HasTokenBudget { get; set; }

    /// <summary>
    /// 获取或设置是否处于 Git 工作区（worktree）。
    /// </summary>
    public bool IsGitWorktree { get; set; }

    /// <summary>
    /// 获取或设置是否启用简单模式。
    /// </summary>
    public bool IsSimpleMode { get; set; }

    /// <summary>
    /// 获取或设置是否启用工具结果清理，默认为 true。
    /// </summary>
    public bool ToolResultClearingEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置自定义介绍文本。
    /// </summary>
    public string? CustomIntro { get; set; }

    /// <summary>
    /// 获取或设置项目规则文本。
    /// </summary>
    public string? ProjectRules { get; set; }

    /// <summary>
    /// 获取或设置外部规则条目列表。
    /// </summary>
    public IReadOnlyList<ExternalRuleEntry>? ExternalRules { get; set; }

    /// <summary>
    /// 获取或设置 MCP 服务器名称集合。
    /// </summary>
    public IEnumerable<string>? McpServers { get; set; }

    /// <summary>
    /// 获取或设置已启用的工具名称集合。
    /// </summary>
    public IEnumerable<string>? EnabledTools { get; set; }

    /// <summary>
    /// 获取或设置草稿板路径。
    /// </summary>
    public string? ScratchpadPath { get; set; }

    /// <summary>
    /// 获取或设置语言偏好。
    /// </summary>
    public string? LanguagePreference { get; set; }

    /// <summary>
    /// 获取或设置模型标识符。
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// 获取或设置模型显示名称。
    /// </summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// 获取或设置版本号。
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 获取或设置构建时间。
    /// </summary>
    public string? BuildTime { get; set; }

    /// <summary>
    /// 获取或设置问题说明文本。
    /// </summary>
    public string? IssuesExplainer { get; set; }

    /// <summary>
    /// 获取或设置反馈渠道。
    /// </summary>
    public string? FeedbackChannel { get; set; }

    /// <summary>
    /// 获取或设置额外工作目录集合。
    /// </summary>
    public IEnumerable<string>? AdditionalWorkdirs { get; set; }

    /// <summary>
    /// 获取或设置额外环境信息。
    /// </summary>
    public string? AdditionalEnvInfo { get; set; }
}