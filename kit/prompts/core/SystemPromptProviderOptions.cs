namespace Core.Prompts;

/// <summary>
/// 系统提示词提供者配置选项
/// </summary>
public partial class SystemPromptProviderOptions {
    #region 运行模式

    /// <summary>
    /// 是否启用 Agent 模式。
    /// </summary>
    public bool IsAgentMode { get; init; }
    /// <summary>
    /// 是否启用 Coordinator（协调者）模式。
    /// </summary>
    public bool IsCoordinatorMode { get; init; }
    /// <summary>
    /// 是否启用 REPL 交互模式。
    /// </summary>
    public bool IsReplMode { get; init; }

    /// <summary>
    /// 从环境变量 JCC_COORDINATOR_MODE 检测是否启用 Coordinator 模式
    /// <para>对齐 TS 原版 CLAUDE_CODE_COORDINATOR_MODE 环境变量</para>
    /// <para>支持值: 1, true, TRUE(不区分大小写)</para>
    /// </summary>
    public static bool IsCoordinatorModeEnabledFromEnv() {
        var value = Environment.GetEnvironmentVariable("JCC_COORDINATOR_MODE");
        return value is "1" or "true" or "TRUE";
    }

    /// <summary>
    /// 从环境变量 JCC_SUBAGENT_MODEL 获取 subagent 模型覆盖
    /// <para>对齐 TS 原版 CLAUDE_CODE_SUBAGENT_MODEL 环境变量</para>
    /// <para>设置后全局覆盖所有 subagent 模型,用于测试/调试</para>
    /// </summary>
    public static string? GetSubagentModelFromEnv() {
        return Environment.GetEnvironmentVariable("JCC_SUBAGENT_MODEL");
    }

    /// <summary>
    /// 判断 agent 指定的 model alias 是否匹配父模型 tier
    /// <para>对齐 TS 原版 aliasMatchesParentTier — 避免 Vertex 用户从 Opus 4.6 降级到默认 Opus</para>
    /// <para>alias = "opus" 且 parentModel 含 "opus" → true(用父模型,避免降级)</para>
    /// <para>委托给 SubAgentModelResolver.AliasMatchesParentTier 保持单一真相源</para>
    /// </summary>
    public static bool ModelAliasMatchesParentTier(string? alias, string parentModel)
        => SubAgentModelResolver.AliasMatchesParentTier(alias, parentModel);

    /// <summary>
    /// 子代理默认模型关键字 — 对齐 TS 原版 getDefaultSubagentModel
    /// <para>返回 "inherit" 表示子代理默认继承父线程模型</para>
    /// </summary>
    public const string DefaultSubagentModel = SubAgentModelResolver.DefaultSubagentModel;

    /// <summary>
    /// 判断模型字符串是否是 inherit 关键字 — 对齐 TS 原版 agentModelWithExp === 'inherit'
    /// <para>不区分大小写: "inherit"、"Inherit"、"INHERIT" 均返回 true</para>
    /// <para>null/空白 返回 false</para>
    /// <para>委托给 SubAgentModelResolver.IsInheritKeyword 保持单一真相源</para>
    /// </summary>
    public static bool IsInheritKeyword(string? model)
        => SubAgentModelResolver.IsInheritKeyword(model);

    /// <summary>
    /// 获取子代理模型显示文本 — 对齐 TS 原版 getAgentModelDisplay
    /// <para>null/空 → "Inherit from parent (default)"</para>
    /// <para>"inherit" → "Inherit from parent"</para>
    /// <para>其他 → 首字母大写</para>
    /// <para>委托给 SubAgentModelResolver.GetAgentModelDisplay 保持单一真相源</para>
    /// </summary>
    public static string GetAgentModelDisplay(string? model)
        => SubAgentModelResolver.GetAgentModelDisplay(model);

    #endregion

    #region 环境信息

    /// <summary>
    /// 附加环境信息文本（追加到环境部分输出）。
    /// </summary>
    public string? AdditionalEnvInfo { get; init; }
    /// <summary>
    /// 语言偏好（如"简体中文"），驱动回复语言。
    /// </summary>
    public string? LanguagePreference { get; init; }
    /// <summary>
    /// 当前模型 ID。
    /// </summary>
    public string? ModelId { get; init; }
    /// <summary>
    /// 当前模型显示名称。
    /// </summary>
    public string? ModelName { get; init; }
    /// <summary>
    /// 应用版本号。
    /// </summary>
    public string? Version { get; init; }
    /// <summary>
    /// 应用构建时间。
    /// </summary>
    public string? BuildTime { get; init; }
    /// <summary>
    /// 是否运行在 Git worktree 中。
    /// </summary>
    public bool IsGitWorktree { get; init; }
    /// <summary>
    /// 额外工作目录列表。
    /// </summary>
    public IEnumerable<string> AdditionalWorkdirs { get; init; } = [];

    #endregion

    #region 工具可用性

    /// <summary>
    /// 已启用的工具名称集合。
    /// </summary>
    public IEnumerable<string> EnabledTools { get; init; } = [];
    /// <summary>
    /// 是否拥有 Todo 工具。
    /// </summary>
    public bool HasTodoTool { get; init; }
    /// <summary>
    /// 是否拥有 Task 工具。
    /// </summary>
    public bool HasTaskTool { get; init; }
    /// <summary>
    /// 是否拥有 Team 工具集。
    /// </summary>
    public bool HasTeamTools { get; init; }
    /// <summary>
    /// 是否拥有 SendMessage 工具。
    /// </summary>
    public bool HasSendMessage { get; init; }
    /// <summary>
    /// 是否启用数字长度限制部分。
    /// </summary>
    public bool EnableNumericLength { get; init; }
    /// <summary>
    /// 是否拥有 Token 预算信息。
    /// </summary>
    public bool HasTokenBudget { get; init; }

    #endregion

    #region 规则与上下文

    /// <summary>
    /// 项目规则文本（如 AGENTS.md 内容）。
    /// </summary>
    public string? ProjectRules { get; init; }
    /// <summary>
    /// 外部规则条目列表。
    /// </summary>
    public IReadOnlyList<ExternalRuleEntry> ExternalRules { get; init; } = [];
    /// <summary>
    /// 文件上下文跟踪器。
    /// </summary>
    public FileContextTracker? FileContext { get; init; }
    /// <summary>
    /// MCP 服务器名称集合。
    /// </summary>
    public IEnumerable<string> McpServers { get; init; } = [];
    /// <summary>
    /// 问题解释器文本。
    /// </summary>
    public string? IssuesExplainer { get; init; }
    /// <summary>
    /// 反馈渠道说明文本。
    /// </summary>
    public string? FeedbackChannel { get; init; }

    #endregion

    #region 记忆与草稿

    /// <summary>
    /// 草稿本文件路径。
    /// </summary>
    public string? ScratchpadPath { get; init; }
    /// <summary>
    /// 离开期间的摘要文本。
    /// </summary>
    public string? AwaySummary { get; init; }
    /// <summary>
    /// 日常日志提示词构建委托。
    /// </summary>
    public Func<Task<string>>? DailyLogPromptBuilder { get; init; }
    /// <summary>
    /// 搜索历史提示词构建委托，参数为查询关键字。
    /// </summary>
    public Func<string, Task<string>>? SearchHistoryPromptBuilder { get; init; }

    #endregion

    #region Agent 相关

    /// <summary>
    /// 伙伴名称。
    /// </summary>
    public string? CompanionName { get; init; }
    /// <summary>
    /// 伙伴物种。
    /// </summary>
    public string? CompanionSpecies { get; init; }
    /// <summary>
    /// Agent 定义列表。
    /// </summary>
    public IReadOnlyList<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> AgentDefinitions { get; init; } = [];

    #endregion

    #region 服务注入

    /// <summary>
    /// 简短模式服务（可选）。
    /// </summary>
    public IBriefModeService? BriefModeService { get; init; }
    /// <summary>
    /// 文件系统抽象（可选）。
    /// </summary>
    public IFileSystem? FileSystem { get; init; }

    /// <summary>
    /// 可选时钟服务 — 供 static PromptSection 读取当前时间（经 PromptConfigSnapshot 传递）
    /// <para>为 null 时 section 回退到 DateTime.Now</para>
    /// </summary>
    public IClockService? Clock { get; init; }

    /// <summary>
    /// 可选日志器 — 供 static PromptSection 读取（经 PromptConfigSnapshot 传递）
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// 所有已注册系统执行器的信息快照 — 通用集合，新增执行器类型无需改代码
    /// Key: SystemActuatorKind, Value: SystemActuatorInfo (DisplayName + ShellPath + Version)
    /// </summary>
    public IReadOnlyDictionary<SystemActuatorKind, SystemActuatorInfo> ShellInfos { get; init; } = new Dictionary<SystemActuatorKind, SystemActuatorInfo>();

    #endregion

    #region 自定义

    /// <summary>
    /// 自定义引言文本。
    /// </summary>
    public string? CustomIntro { get; init; }

    #endregion

    /// <summary>
    /// 获取默认配置实例。
    /// </summary>
    public static SystemPromptProviderOptions Default => new();

    /// <summary>
    /// 创建 Agent 模式配置实例。
    /// </summary>
    /// <param name="projectRules">项目规则文本。</param>
    /// <param name="enabledTools">已启用的工具集合。</param>
    /// <param name="languagePreference">语言偏好。</param>
    /// <returns>标记为 Agent 模式的配置实例。</returns>
    public static SystemPromptProviderOptions ForAgentMode(
        string? projectRules = null,
        IEnumerable<string>? enabledTools = null,
        string? languagePreference = null) {
        return new SystemPromptProviderOptions {
            IsAgentMode = true,
            ProjectRules = projectRules,
            EnabledTools = enabledTools ?? [],
            LanguagePreference = languagePreference
        };
    }

    /// <summary>
    /// 创建 Coordinator（协调者）模式配置实例。
    /// </summary>
    /// <param name="agentDefinitions">Agent 定义列表。</param>
    /// <param name="projectRules">项目规则文本。</param>
    /// <param name="enabledTools">已启用的工具集合。</param>
    /// <param name="languagePreference">语言偏好。</param>
    /// <returns>标记为 Coordinator 模式的配置实例。</returns>
    public static SystemPromptProviderOptions ForCoordinatorMode(
        IReadOnlyList<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>? agentDefinitions = null,
        string? projectRules = null,
        IEnumerable<string>? enabledTools = null,
        string? languagePreference = null) {
        return new SystemPromptProviderOptions {
            IsCoordinatorMode = true,
            AgentDefinitions = agentDefinitions ?? [],
            ProjectRules = projectRules,
            EnabledTools = enabledTools ?? [],
            LanguagePreference = languagePreference
        };
    }
}