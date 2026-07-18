
namespace Core.Configuration;

/// <summary>
/// settings.json 强类型 — 直接映射 JSON 文件结构，对齐 TS 版 SettingsSchema
/// 所有字段 nullable + init，因为 JSON 中字段可能缺失
/// [SettingsMerge] 源码生成器自动生成: 拷贝构造函数、Merge、GetSettingByKey、UpdateSettingByKey
/// </summary>
[SettingsMerge]
public sealed partial class SettingsJson
{
    /// <summary>
    /// 默认构造函数
    /// </summary>
    public SettingsJson() { }

    /// <summary>
    /// JSON Schema URL（可选，用于 IDE 智能提示）
    /// </summary>
    [SettingsProperty(SettingsMergeStrategy.Override, SkipCopy = true, SkipMerge = true, SkipKeyAccess = true)]
    public string? Schema { get; init; }

    /// <summary>
    /// 默认模型 ID（如 "gpt-4o"、"claude-sonnet-4-20250514"）
    /// </summary>
    [JsonPropertyName("model")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? Model { get; init; }

    /// <summary>
    /// 默认 Provider（如 "openai"、"anthropic"）
    /// </summary>
    [JsonPropertyName("provider")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? Provider { get; init; }

    /// <summary>
    /// Provider Endpoint（如 Azure OpenAI 的 https://your-resource.openai.azure.com）
    /// </summary>
    [JsonPropertyName("endpoint")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? Endpoint { get; init; }

    /// <summary>
    /// 推理努力级别: low, medium, high
    /// </summary>
    [JsonPropertyName("effortLevel")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? EffortLevel { get; init; }

    /// <summary>
    /// 默认 Shell: bash, powershell
    /// </summary>
    [JsonPropertyName("defaultShell")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? DefaultShell { get; init; }

    /// <summary>
    /// 快速模式（使用更小/更快的模型）
    /// </summary>
    [JsonPropertyName("fastMode")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? FastMode { get; init; }

    /// <summary>
    /// 语言设置（如 "zh-CN"、"en-US"）
    /// </summary>
    [JsonPropertyName("language")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? Language { get; init; }

    /// <summary>
    /// 自动记忆功能
    /// </summary>
    [JsonPropertyName("autoMemoryEnabled")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? AutoMemoryEnabled { get; init; }

    /// <summary>
    /// 自动 Dream 功能
    /// </summary>
    [JsonPropertyName("autoDreamEnabled")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? AutoDreamEnabled { get; init; }

    /// <summary>
    /// 显示思考摘要
    /// </summary>
    [JsonPropertyName("showThinkingSummaries")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? ShowThinkingSummaries { get; init; }

    /// <summary>
    /// 环境变量注入（替代 .env 文件）— 对齐 TS 版 env 字段
    /// 键: 环境变量名, 值: 环境变量值
    /// </summary>
    [JsonPropertyName("env")]
    [SettingsProperty(SettingsMergeStrategy.DictionaryMerge, DictionaryValueType = "string", SkipKeyAccess = true)]
    public Dictionary<string, string>? Env { get; init; }

    /// <summary>
    /// 权限配置 — 对齐 TS 版 PermissionsSchema
    /// </summary>
    [JsonPropertyName("permissions")]
    [SettingsProperty(SettingsMergeStrategy.RecursiveMerge, CustomMergeMethod = "MergePermissions", SkipKeyAccess = true)]
    public PermissionsSettings? Permissions { get; init; }

    /// <summary>
    /// Hook 配置 — 对齐 TS 版 HooksSchema
    /// 键: Hook 事件名 (PreToolUse, PostToolUse 等), 值: Hook 列表
    /// </summary>
    [JsonPropertyName("hooks")]
    [SettingsProperty(SettingsMergeStrategy.Custom, CustomMergeMethod = "MergeHookDictionaries", SkipKeyAccess = true)]
    public Dictionary<string, List<HookSettings>>? Hooks { get; init; }

    /// <summary>
    /// MCP 服务器配置 — 对齐 TS 版 mcpServers
    /// 键: 服务器名, 值: 服务器配置
    /// </summary>
    [JsonPropertyName("mcpServers")]
    [SettingsProperty(SettingsMergeStrategy.DictionaryMerge, DictionaryValueType = "McpServerSettings", SkipKeyAccess = true)]
    public Dictionary<string, McpServerSettings>? McpServers { get; init; }

    /// <summary>
    /// 沙箱配置 — 对齐 TS 版 SandboxSettingsSchema
    /// </summary>
    [JsonPropertyName("sandbox")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public SandboxSettings? Sandbox { get; init; }

    /// <summary>
    /// 插件配置 — 对齐 TS 版 enabledPlugins
    /// 键: 插件名, 值: 插件配置
    /// </summary>
    [JsonPropertyName("enabledPlugins")]
    [SettingsProperty(SettingsMergeStrategy.DictionaryMerge, DictionaryValueType = "PluginSettings", SkipKeyAccess = true)]
    public Dictionary<string, PluginSettings>? EnabledPlugins { get; init; }

    /// <summary>
    /// API Key 辅助命令 — 对齐 TS 版 apiKeyHelper
    /// </summary>
    [JsonPropertyName("apiKeyHelper")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? ApiKeyHelper { get; init; }

    /// <summary>
    /// 是否尊重 .gitignore — 对齐 TS 版 respectGitignore
    /// </summary>
    [JsonPropertyName("respectGitignore")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? RespectGitignore { get; init; }

    /// <summary>
    /// 清理周期（天）— 对齐 TS 版 cleanupPeriodDays
    /// </summary>
    [JsonPropertyName("cleanupPeriodDays")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public int? CleanupPeriodDays { get; init; }

    /// <summary>
    /// 是否包含 Git 共同作者 — 对齐 TS 版 includeCoAuthoredBy
    /// </summary>
    [JsonPropertyName("includeCoAuthoredBy")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? IncludeCoAuthoredBy { get; init; }

    /// <summary>
    /// 是否包含 Git 指令 — 对齐 TS 版 includeGitInstructions
    /// </summary>
    [JsonPropertyName("includeGitInstructions")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? IncludeGitInstructions { get; init; }

    /// <summary>
    /// 可用模型列表 — 对齐 TS 版 availableModels
    /// </summary>
    [JsonPropertyName("availableModels")]
    [SettingsProperty(SettingsMergeStrategy.ListConcatDistinct, SkipKeyAccess = true)]
    public List<string>? AvailableModels { get; init; }

    /// <summary>
    /// 模型覆盖映射 — 对齐 TS 版 modelOverrides
    /// 键: 原始模型名, 值: 覆盖模型名
    /// </summary>
    [JsonPropertyName("modelOverrides")]
    [SettingsProperty(SettingsMergeStrategy.DictionaryMerge, DictionaryValueType = "string", SkipKeyAccess = true)]
    public Dictionary<string, string>? ModelOverrides { get; init; }

    /// <summary>
    /// 是否启用所有项目 MCP 服务器 — 对齐 TS 版 enableAllProjectMcpServers
    /// </summary>
    [JsonPropertyName("enableAllProjectMcpServers")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? EnableAllProjectMcpServers { get; init; }

    /// <summary>
    /// 已启用的 MCP JSON 服务器 — 对齐 TS 版 enabledMcpjsonServers
    /// </summary>
    [JsonPropertyName("enabledMcpjsonServers")]
    [SettingsProperty(SettingsMergeStrategy.ListConcatDistinct, SkipKeyAccess = true)]
    public List<string>? EnabledMcpjsonServers { get; init; }

    /// <summary>
    /// 已禁用的 MCP JSON 服务器 — 对齐 TS 版 disabledMcpjsonServers
    /// </summary>
    [JsonPropertyName("disabledMcpjsonServers")]
    [SettingsProperty(SettingsMergeStrategy.ListConcatDistinct, SkipKeyAccess = true)]
    public List<string>? DisabledMcpjsonServers { get; init; }

    /// <summary>
    /// 是否禁用所有 Hook — 对齐 TS 版 disableAllHooks
    /// </summary>
    [JsonPropertyName("disableAllHooks")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? DisableAllHooks { get; init; }

    /// <summary>
    /// Worktree 配置 — 对齐 TS 版 worktree
    /// </summary>
    [JsonPropertyName("worktree")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public WorktreeSettings? Worktree { get; init; }

    /// <summary>
    /// 活跃 Worktree 会话 — 对齐 TS 版 activeWorktreeSession，持久化到 settings.local.json
    /// </summary>
    [JsonPropertyName("activeWorktreeSession")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public ActiveWorktreeSessionJson? ActiveWorktreeSession { get; init; }

    /// <summary>
    /// 状态栏配置 — 对齐 TS 版 statusLine
    /// </summary>
    [JsonPropertyName("statusLine")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public StatusLineSettings? StatusLine { get; init; }

    /// <summary>
    /// 输出风格 — 对齐 TS 版 outputStyle
    /// </summary>
    [JsonPropertyName("outputStyle")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? OutputStyle { get; init; }

    #region 自定义合并方法

    /// <summary>
    /// 合并权限配置 — 递归合并 Allow/Deny/Ask 列表
    /// </summary>
    private static PermissionsSettings? MergePermissions(PermissionsSettings? basePerms, PermissionsSettings? overridePerms)
    {
        if (basePerms is null && overridePerms is null) return null;
        if (basePerms is null) return overridePerms;
        if (overridePerms is null) return basePerms;

        return new PermissionsSettings
        {
            Allow = MergeLists(basePerms.Allow, overridePerms.Allow),
            Deny = MergeLists(basePerms.Deny, overridePerms.Deny),
            Ask = MergeLists(basePerms.Ask, overridePerms.Ask),
            DefaultMode = overridePerms.DefaultMode ?? basePerms.DefaultMode,
            AdditionalDirectories = MergeLists(basePerms.AdditionalDirectories, overridePerms.AdditionalDirectories),
            DisableBypassPermissionsMode = overridePerms.DisableBypassPermissionsMode ?? basePerms.DisableBypassPermissionsMode,
        };
    }

    /// <summary>
    /// 合并 Hook 字典 — 拼接同键的 Hook 列表
    /// </summary>
    private static Dictionary<string, List<HookSettings>>? MergeHookDictionaries(
        Dictionary<string, List<HookSettings>>? baseHooks,
        Dictionary<string, List<HookSettings>>? overrideHooks)
    {
        if (baseHooks is null && overrideHooks is null) return null;
        if (baseHooks is null) return overrideHooks;
        if (overrideHooks is null) return baseHooks;

        var result = new Dictionary<string, List<HookSettings>>(baseHooks, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrideHooks)
        {
            if (result.TryGetValue(key, out var existing))
            {
                result[key] = existing.Concat(value).ToList();
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    #endregion
}

/// <summary>
/// 权限配置 — 对齐 TS 版 PermissionsSchema
/// </summary>
public sealed class PermissionsSettings
{
    [JsonPropertyName("allow")]
    public List<string>? Allow { get; init; }

    [JsonPropertyName("deny")]
    public List<string>? Deny { get; init; }

    [JsonPropertyName("ask")]
    public List<string>? Ask { get; init; }

    /// <summary>
    /// 默认权限模式: default, plan, autoAccept
    /// </summary>
    [JsonPropertyName("defaultMode")]
    public string? DefaultMode { get; init; }

    /// <summary>
    /// 额外允许的目录
    /// </summary>
    [JsonPropertyName("additionalDirectories")]
    public List<string>? AdditionalDirectories { get; init; }

    /// <summary>
    /// 是否禁用绕过权限模式 — 对齐 TS 版 disableBypassPermissionsMode
    /// </summary>
    [JsonPropertyName("disableBypassPermissionsMode")]
    public string? DisableBypassPermissionsMode { get; init; }
}

/// <summary>
/// Hook 配置项 — 对齐 TS 版 HookSchema
/// </summary>
public sealed class HookSettings
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("matcher")]
    public string? Matcher { get; init; }

    [JsonPropertyName("timeout")]
    public int? Timeout { get; init; }

    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; init; }

    [JsonPropertyName("once")]
    public bool? Once { get; init; }
}

/// <summary>
/// MCP 服务器配置 — 对齐 TS 版 McpServerConfig
/// </summary>
public sealed class McpServerSettings
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// 沙箱配置 — 对齐 TS 版 SandboxSettingsSchema
/// </summary>
public sealed class SandboxSettings
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("image")]
    public string? Image { get; init; }
}

/// <summary>
/// 插件配置项 — 对齐 TS 版 PluginSettings
/// </summary>
public sealed class PluginSettings
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("config")]
    public Dictionary<string, string>? Config { get; init; }
}

/// <summary>
/// Worktree 配置 — 对齐 TS 版 worktree
/// </summary>
public sealed class WorktreeSettings
{
    [JsonPropertyName("symlinkDirectories")]
    public List<string>? SymlinkDirectories { get; init; }

    [JsonPropertyName("sparsePaths")]
    public List<string>? SparsePaths { get; init; }
}

/// <summary>
/// 活跃 Worktree 会话 — 对齐 TS 版 activeWorktreeSession，持久化到 settings.local.json
/// </summary>
public sealed class ActiveWorktreeSessionJson
{
    [JsonPropertyName("originalCwd")]
    public string? OriginalCwd { get; init; }

    [JsonPropertyName("worktreePath")]
    public string? WorktreePath { get; init; }

    [JsonPropertyName("worktreeName")]
    public string? WorktreeName { get; init; }

    [JsonPropertyName("worktreeBranch")]
    public string? WorktreeBranch { get; init; }

    [JsonPropertyName("originalBranch")]
    public string? OriginalBranch { get; init; }

    [JsonPropertyName("originalHeadCommit")]
    public string? OriginalHeadCommit { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("hookBased")]
    public bool? HookBased { get; init; }

    [JsonPropertyName("creationDurationMs")]
    public long? CreationDurationMs { get; init; }

    [JsonPropertyName("usedSparsePaths")]
    public bool? UsedSparsePaths { get; init; }
}

/// <summary>
/// 状态栏配置 — 对齐 TS 版 statusLine
/// </summary>
public sealed class StatusLineSettings
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("padding")]
    public int? Padding { get; init; }
}
