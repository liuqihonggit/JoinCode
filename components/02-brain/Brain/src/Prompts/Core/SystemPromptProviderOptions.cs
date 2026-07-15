namespace Core.Prompts;

/// <summary>
/// 系统提示词提供者配置选项
/// </summary>
public partial class SystemPromptProviderOptions
{
    #region 运行模式

    public bool IsAgentMode { get; init; }
    public bool IsCoordinatorMode { get; init; }
    public bool IsReplMode { get; init; }

    #endregion

    #region 环境信息

    public string? AdditionalEnvInfo { get; init; }
    public string? LanguagePreference { get; init; }
    public string? ModelId { get; init; }
    public string? ModelName { get; init; }
    public string? Version { get; init; }
    public string? BuildTime { get; init; }
    public bool IsGitWorktree { get; init; }
    public IEnumerable<string>? AdditionalWorkdirs { get; init; }

    #endregion

    #region 工具可用性

    public IEnumerable<string>? EnabledTools { get; init; }
    public bool HasTodoTool { get; init; }
    public bool HasTaskTool { get; init; }
    public bool HasTeamTools { get; init; }
    public bool HasSendMessage { get; init; }
    public bool EnableNumericLength { get; init; }
    public bool HasTokenBudget { get; init; }

    #endregion

    #region 规则与上下文

    public string? ProjectRules { get; init; }
    public IReadOnlyList<ExternalRuleEntry>? ExternalRules { get; init; }
    public FileContextTracker? FileContext { get; init; }
    public IEnumerable<string>? McpServers { get; init; }
    public string? IssuesExplainer { get; init; }
    public string? FeedbackChannel { get; init; }

    #endregion

    #region 记忆与草稿

    public string? ScratchpadPath { get; init; }
    public string? AwaySummary { get; init; }
    public Func<Task<string>>? DailyLogPromptBuilder { get; init; }
    public Func<string, Task<string>>? SearchHistoryPromptBuilder { get; init; }

    #endregion

    #region Agent 相关

    public string? CompanionName { get; init; }
    public string? CompanionSpecies { get; init; }
    public IReadOnlyList<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>? AgentDefinitions { get; init; }

    #endregion

    #region 服务注入

    public IBriefModeService? BriefModeService { get; init; }
    public IFileSystem? FileSystem { get; init; }

    /// <summary>
    /// Bash Shell 版本信息 — 由 BashShellProvider.Version 检测后注入
    /// </summary>
    public string? BashVersion { get; init; }

    /// <summary>
    /// PowerShell 版本信息 — 由 PowerShellShellProvider.Version 检测后注入
    /// </summary>
    public string? PowerShellVersion { get; init; }

    /// <summary>
    /// PowerShell 版本类别 — "desktop"(5.1) / "core"(7+) / null
    /// </summary>
    public string? PowerShellEdition { get; init; }

    /// <summary>
    /// Bash 可执行文件路径 — 让 LLM 知道具体使用哪个 shell
    /// </summary>
    public string? BashPath { get; init; }

    /// <summary>
    /// PowerShell 可执行文件路径 — 让 LLM 知道具体使用哪个 shell
    /// </summary>
    public string? PowerShellPath { get; init; }

    #endregion

    #region 自定义

    public string? CustomIntro { get; init; }

    #endregion

    public static SystemPromptProviderOptions Default => new();

    public static SystemPromptProviderOptions ForAgentMode(
        string? projectRules = null,
        IEnumerable<string>? enabledTools = null,
        string? languagePreference = null)
    {
        return new SystemPromptProviderOptions
        {
            IsAgentMode = true,
            ProjectRules = projectRules,
            EnabledTools = enabledTools,
            LanguagePreference = languagePreference
        };
    }

    public static SystemPromptProviderOptions ForCoordinatorMode(
        IReadOnlyList<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>? agentDefinitions = null,
        string? projectRules = null,
        IEnumerable<string>? enabledTools = null,
        string? languagePreference = null)
    {
        return new SystemPromptProviderOptions
        {
            IsCoordinatorMode = true,
            AgentDefinitions = agentDefinitions,
            ProjectRules = projectRules,
            EnabledTools = enabledTools,
            LanguagePreference = languagePreference
        };
    }
}
