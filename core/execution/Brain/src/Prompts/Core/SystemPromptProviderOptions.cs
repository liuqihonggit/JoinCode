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

    /// <summary>
    /// 从环境变量 JCC_COORDINATOR_MODE 检测是否启用 Coordinator 模式
    /// <para>对齐 claude code CLAUDE_CODE_COORDINATOR_MODE 环境变量</para>
    /// <para>支持值: 1, true, TRUE(不区分大小写)</para>
    /// </summary>
    public static bool IsCoordinatorModeEnabledFromEnv()
    {
        var value = Environment.GetEnvironmentVariable("JCC_COORDINATOR_MODE");
        return value is "1" or "true" or "TRUE";
    }

    /// <summary>
    /// 从环境变量 JCC_SUBAGENT_MODEL 获取 subagent 模型覆盖
    /// <para>对齐 claude code CLAUDE_CODE_SUBAGENT_MODEL 环境变量</para>
    /// <para>设置后全局覆盖所有 subagent 模型,用于测试/调试</para>
    /// </summary>
    public static string? GetSubagentModelFromEnv()
    {
        return Environment.GetEnvironmentVariable("JCC_SUBAGENT_MODEL");
    }

    /// <summary>
    /// 判断 agent 指定的 model alias 是否匹配父模型 tier
    /// <para>对齐 claude code aliasMatchesParentTier — 避免 Vertex 用户从 Opus 4.6 降级到默认 Opus</para>
    /// <para>alias = "opus" 且 parentModel 含 "opus" → true(用父模型,避免降级)</para>
    /// <para>委托给 SubAgentModelResolver.AliasMatchesParentTier 保持单一真相源</para>
    /// </summary>
    public static bool ModelAliasMatchesParentTier(string? alias, string parentModel)
        => SubAgentModelResolver.AliasMatchesParentTier(alias, parentModel);

    /// <summary>
    /// 子代理默认模型关键字 — 对齐 claude code getDefaultSubagentModel
    /// <para>返回 "inherit" 表示子代理默认继承父线程模型</para>
    /// </summary>
    public const string DefaultSubagentModel = SubAgentModelResolver.DefaultSubagentModel;

    /// <summary>
    /// 判断模型字符串是否是 inherit 关键字 — 对齐 claude code agentModelWithExp === 'inherit'
    /// <para>不区分大小写: "inherit"、"Inherit"、"INHERIT" 均返回 true</para>
    /// <para>null/空白 返回 false</para>
    /// <para>委托给 SubAgentModelResolver.IsInheritKeyword 保持单一真相源</para>
    /// </summary>
    public static bool IsInheritKeyword(string? model)
        => SubAgentModelResolver.IsInheritKeyword(model);

    /// <summary>
    /// 获取子代理模型显示文本 — 对齐 claude code getAgentModelDisplay
    /// <para>null/空 → "Inherit from parent (default)"</para>
    /// <para>"inherit" → "Inherit from parent"</para>
    /// <para>其他 → 首字母大写</para>
    /// <para>委托给 SubAgentModelResolver.GetAgentModelDisplay 保持单一真相源</para>
    /// </summary>
    public static string GetAgentModelDisplay(string? model)
        => SubAgentModelResolver.GetAgentModelDisplay(model);

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
    /// 可选日志器 — 供 static PromptSection 读取（经 PromptConfigSnapshot 传递）
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// 所有已注册系统执行器的信息快照 — 通用集合，新增执行器类型无需改代码
    /// Key: SystemActuatorKind, Value: SystemActuatorInfo (DisplayName + ShellPath + Version)
    /// </summary>
    public IReadOnlyDictionary<SystemActuatorKind, SystemActuatorInfo>? ShellInfos { get; init; }

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
