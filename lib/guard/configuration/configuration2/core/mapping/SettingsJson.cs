
namespace Core.Configuration;

/// <summary>
/// settings.json 强类型 — 顶层只有 vendor 和 current 两个分支
/// vendor: 供应商预设字典（键为供应商名，值为 provider/model/endpoint 组合）
/// current: 当前正在使用的运行时配置（包含 profile 指针 + 所有偏好设置）
/// </summary>
public sealed class SettingsJson
{
    /// <summary>初始化空的 SettingsJson 实例</summary>
    public SettingsJson() { }

    /// <summary>
    /// 供应商预设 — 命名的供应商/模型/端点组合，用户通过 current.profile 或 --vendor 切换
    /// 键: 供应商名（如 "sensenova"、"agnes"），值: 预设配置
    /// </summary>
    [JsonPropertyName("vendor")]
    public Dictionary<string, ProfileSettings> Vendor { get; init; } = [];

    /// <summary>
    /// 当前正在使用的运行时配置 — 包含 profile 指针和所有偏好设置
    /// </summary>
    [JsonPropertyName("current")]
    public CurrentSettings? Current { get; init; }

    /// <summary>
    /// 是否在启动时自动从远程拉取各供应商模型列表 — 默认 true。false 时跳过拉取（离线环境）。
    /// </summary>
    [JsonPropertyName("autoFetchModels")]
    public bool AutoFetchModels { get; init; } = true;

    /// <summary>
    /// 更新源配置 — 控制自动更新行为（ADR 0064）
    /// </summary>
    [JsonPropertyName("update")]
    public UpdateSourceConfig? Update { get; init; }

    /// <summary>
    /// 合并两个 SettingsJson（低优先级 + 高优先级）
    /// vendor 字典合并（高优先级覆盖同键），current 递归合并
    /// </summary>
    public static SettingsJson Merge(SettingsJson? baseSettings, SettingsJson? overrideSettings)
    {
        if (baseSettings is null) return overrideSettings ?? new SettingsJson();
        if (overrideSettings is null) return baseSettings;

        return new SettingsJson
        {
            Vendor = MergeVendorDictionaries(baseSettings.Vendor, overrideSettings.Vendor) ?? [],
            Current = CurrentSettings.Merge(baseSettings.Current, overrideSettings.Current),
            AutoFetchModels = overrideSettings.AutoFetchModels,
            Update = overrideSettings.Update ?? baseSettings.Update,
        };
    }

    /// <summary>
    /// 获取当前激活的供应商预设 — 从 current.profile 指向 vendor 字典的键
    /// </summary>
    public ProfileSettings? GetActiveProfile()
    {
        if (Current is null || string.IsNullOrEmpty(Current.Profile)) return null;
        if (Vendor is null || !Vendor.TryGetValue(Current.Profile, out var profile)) return null;
        return profile;
    }

    private static Dictionary<string, ProfileSettings>? MergeVendorDictionaries(
        Dictionary<string, ProfileSettings>? baseDict,
        Dictionary<string, ProfileSettings>? overrideDict)
    {
        if (baseDict is null && overrideDict is null) return null;
        if (baseDict is null) return overrideDict;
        if (overrideDict is null) return baseDict;

        var result = new Dictionary<string, ProfileSettings>(baseDict, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrideDict)
            result[key] = value;

        return result;
    }
}

/// <summary>
/// 当前运行时配置 — settings.json 的 current 分支
/// 包含 profile 指针（指向 vendor 字典中的键）和所有偏好设置
/// [SettingsMerge] 源码生成器自动生成: 拷贝构造函数、Merge、GetSettingByKey、UpdateSettingByKey
/// </summary>
[SettingsMerge]
public sealed partial class CurrentSettings
{
    /// <summary>初始化空的 CurrentSettings 实例</summary>
    public CurrentSettings() { }

    /// <summary>
    /// 当前激活的供应商预设名 — 对应 vendor 字典中的键
    /// 设置后，vendor[profile] 中的 provider/model/endpoint 作为当前连接配置
    /// </summary>
    [JsonPropertyName("profile")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? Profile { get; init; }

    /// <summary>
    /// 推理努力级别: low, medium, high
    /// </summary>
    [JsonPropertyName("effortLevel")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? EffortLevel { get; init; }

    /// <summary>
    /// UI 主题 (dark/light/auto/daltonized/ansi) — 对齐 CLI /theme、ConfigKey.Theme
    /// </summary>
    [JsonPropertyName("theme")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? Theme { get; init; }

    /// <summary>
    /// 按键绑定模式 (vim/emacs/default) — 对齐 ConfigKey.EditorMode
    /// </summary>
    [JsonPropertyName("editorMode")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public string? EditorMode { get; init; }

    /// <summary>
    /// 调试日志输出 — 对齐 ConfigKey.DebugLog
    /// </summary>
    [JsonPropertyName("debuglog")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? DebugLog { get; init; }

    /// <summary>
    /// 自动压缩上下文 — 对齐 ConfigKey.AutoCompactEnabled
    /// </summary>
    [JsonPropertyName("autoCompactEnabled")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? AutoCompactEnabled { get; init; }

    /// <summary>
    /// 文件检查点 — 对齐 ConfigKey.FileCheckpointingEnabled
    /// </summary>
    [JsonPropertyName("fileCheckpointingEnabled")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? FileCheckpointingEnabled { get; init; }

    /// <summary>
    /// 显示轮次耗时 — 对齐 ConfigKey.ShowTurnDuration
    /// </summary>
    [JsonPropertyName("showTurnDuration")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? ShowTurnDuration { get; init; }

    /// <summary>
    /// 扩展思考 — 对齐 ConfigKey.AlwaysThinkingEnabled
    /// </summary>
    [JsonPropertyName("alwaysThinkingEnabled")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? AlwaysThinkingEnabled { get; init; }

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
    public Dictionary<string, string> Env { get; init; } = [];

    /// <summary>
    /// 无人值守模式 — AI 持续推进长任务，红灯自动执行+审计日志，黑灯仍拒绝 — ADR 0012
    /// </summary>
    [JsonPropertyName("isUnattendedMode")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? IsUnattendedMode { get; init; }

    /// <summary>
    /// 防丢字符二次确认 — 防止 MTP 加速推理时丢字符/乱入字符导致命令变形 — ADR 0012
    /// </summary>
    [JsonPropertyName("isAntiCharLossConfirm")]
    [SettingsProperty(SettingsMergeStrategy.Override)]
    public bool? IsAntiCharLossConfirm { get; init; }

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
    public Dictionary<string, List<HookSettings>> Hooks { get; init; } = [];

    /// <summary>
    /// MCP 服务器配置 — 对齐 TS 版 mcpServers
    /// 键: 服务器名, 值: 服务器配置
    /// </summary>
    [JsonPropertyName("mcpServers")]
    [SettingsProperty(SettingsMergeStrategy.DictionaryMerge, DictionaryValueType = "McpServerSettings", SkipKeyAccess = true)]
    public Dictionary<string, McpServerSettings> McpServers { get; init; } = [];

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
    public Dictionary<string, PluginSettings> EnabledPlugins { get; init; } = [];

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
    public List<string> AvailableModels { get; init; } = [];

    /// <summary>
    /// 模型覆盖映射 — 对齐 TS 版 modelOverrides
    /// 键: 原始模型名, 值: 覆盖模型名
    /// </summary>
    [JsonPropertyName("modelOverrides")]
    [SettingsProperty(SettingsMergeStrategy.DictionaryMerge, DictionaryValueType = "string", SkipKeyAccess = true)]
    public Dictionary<string, string> ModelOverrides { get; init; } = [];

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
    public List<string> EnabledMcpjsonServers { get; init; } = [];

    /// <summary>
    /// 已禁用的 MCP JSON 服务器 — 对齐 TS 版 disabledMcpjsonServers
    /// </summary>
    [JsonPropertyName("disabledMcpjsonServers")]
    [SettingsProperty(SettingsMergeStrategy.ListConcatDistinct, SkipKeyAccess = true)]
    public List<string> DisabledMcpjsonServers { get; init; } = [];

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

    /// <summary>
    /// 工具评分配置 — 对齐 CS 版 ToolScoreSettings
    /// </summary>
    [JsonPropertyName("toolScore")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public ToolScoreSettingsJson? ToolScore { get; init; }

    /// <summary>
    /// 工具黑名单 — 用户主动禁用的工具列表
    /// </summary>
    [JsonPropertyName("blacklistedTools")]
    [SettingsProperty(SettingsMergeStrategy.ListConcatDistinct)]
    public List<string> BlacklistedTools { get; init; } = [];

    /// <summary>
    /// 工具降权配置 — 键为工具名，值为额外扣分
    /// </summary>
    [JsonPropertyName("toolPenalties")]
    [SettingsProperty(SettingsMergeStrategy.DictionaryMerge, DictionaryValueType = "int", SkipKeyAccess = true)]
    public Dictionary<string, int> ToolPenalties { get; init; } = [];

    /// <summary>
    /// 自定义超边配置 — 用户自定义工具链超图超边
    /// </summary>
    [JsonPropertyName("customHyperedges")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public List<HyperedgeSettings> CustomHyperedges { get; init; } = [];

    /// <summary>
    /// 搜索范围安全配置 — 控制搜索命令的危险标志和过大路径检测
    /// </summary>
    [JsonPropertyName("searchScope")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public SearchScopeSettings? SearchScope { get; init; }

    /// <summary>
    /// 模型选择历史 — 按最近使用排序，用于模态不匹配时自动切换的首选依据。
    /// 每次用户切换模型时，将模型ID追加到列表头部（去重），最多保留20条。
    /// 格式: ["gpt-5.6-terra", "deepseek-r1", ...]
    /// </summary>
    [JsonPropertyName("modelHistory")]
    [SettingsProperty(SettingsMergeStrategy.ListConcatDistinct)]
    public List<string> ModelHistory { get; init; } = [];

    /// <summary>
    /// 子代理并发控制配置 — spawn/execute/fork 三阶段上限（ADR 0048）
    /// 热重载：settings.json 变更时通过 SubAgentConcurrencyMiddleware 实时更新各组件 SemaphoreSlim
    /// </summary>
    [JsonPropertyName("subAgentConcurrency")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public SubAgentConcurrencyOptions? SubAgentConcurrency { get; init; }

    /// <summary>
    /// 子代理卡死防护配置 — 纵深防御四层参数（ADR 0106）
    /// L1 预防(工具超时) + L2 检测(无输出+巡查+链路) + L3 干预(激活+抢塞) + L4 恢复(渐进式压缩)
    /// </summary>
    [JsonPropertyName("subAgentLiveness")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public SubAgentLivenessOptions? SubAgentLiveness { get; init; }

    /// <summary>
    /// Actor 模型统一配置 — 编译队列模式 + 背压预设(ADR 0074)
    /// settings.json 的 current.actor 节点,缺失时用默认值(串行模式 + 四档预设)
    /// </summary>
    [JsonPropertyName("actor")]
    [SettingsProperty(SettingsMergeStrategy.Override, SkipKeyAccess = true)]
    public ActorSettings? Actor { get; init; }

    #region 自定义合并方法

    private static PermissionsSettings? MergePermissions(PermissionsSettings? basePerms, PermissionsSettings? overridePerms)
    {
        if (basePerms is null && overridePerms is null) return null;
        if (basePerms is null) return overridePerms;
        if (overridePerms is null) return basePerms;

        return new PermissionsSettings
        {
            Allow = MergeLists(basePerms.Allow, overridePerms.Allow) ?? [],
            Deny = MergeLists(basePerms.Deny, overridePerms.Deny) ?? [],
            Ask = MergeLists(basePerms.Ask, overridePerms.Ask) ?? [],
            DefaultMode = overridePerms.DefaultMode ?? basePerms.DefaultMode,
            AdditionalDirectories = MergeLists(basePerms.AdditionalDirectories, overridePerms.AdditionalDirectories) ?? [],
            DisableBypassPermissionsMode = overridePerms.DisableBypassPermissionsMode ?? basePerms.DisableBypassPermissionsMode,
            ToolOverrides = MergeToolOverrides(basePerms.ToolOverrides, overridePerms.ToolOverrides) ?? [],
        };
    }

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

    private static Dictionary<string, ToolOverrideEntry>? MergeToolOverrides(
        Dictionary<string, ToolOverrideEntry>? baseOverrides,
        Dictionary<string, ToolOverrideEntry>? overrideOverrides)
    {
        if (baseOverrides is null && overrideOverrides is null) return null;
        if (baseOverrides is null) return overrideOverrides;
        if (overrideOverrides is null) return baseOverrides;

        var result = new Dictionary<string, ToolOverrideEntry>(baseOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrideOverrides)
        {
            if (result.TryGetValue(key, out var existing))
            {
                result[key] = new ToolOverrideEntry
                {
                    Allow = MergeLists(existing.Allow, value.Allow) ?? [],
                    Deny = MergeLists(existing.Deny, value.Deny) ?? [],
                };
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
    /// <summary>允许的工具匹配规则列表</summary>
    [JsonPropertyName("allow")]
    public List<string> Allow { get; init; } = [];

    /// <summary>拒绝的工具匹配规则列表</summary>
    [JsonPropertyName("deny")]
    public List<string> Deny { get; init; } = [];

    /// <summary>需要询问确认的工具匹配规则列表</summary>
    [JsonPropertyName("ask")]
    public List<string> Ask { get; init; } = [];

    /// <summary>
    /// 默认权限模式: default, plan, autoAccept
    /// </summary>
    [JsonPropertyName("defaultMode")]
    public string? DefaultMode { get; init; }

    /// <summary>
    /// 额外允许的目录
    /// </summary>
    [JsonPropertyName("additionalDirectories")]
    public List<string> AdditionalDirectories { get; init; } = [];

    /// <summary>
    /// 是否禁用绕过权限模式 — 对齐 TS 版 disableBypassPermissionsMode
    /// </summary>
    [JsonPropertyName("disableBypassPermissionsMode")]
    public string? DisableBypassPermissionsMode { get; init; }

    /// <summary>
    /// 工具白名单/黑名单覆盖 — 增量合并到硬编码默认值
    /// 格式: { "auto": { "allow": ["bash"], "deny": [] }, "plan": { "deny": ["web_fetch"] } }
    /// </summary>
    [JsonPropertyName("toolOverrides")]
    public Dictionary<string, ToolOverrideEntry> ToolOverrides { get; init; } = [];
}

/// <summary>
/// 单个模式的工具覆盖 — 增量合并到硬编码默认值
/// </summary>
public sealed class ToolOverrideEntry
{
    /// <summary>该模式下额外允许的工具列表</summary>
    [JsonPropertyName("allow")]
    public List<string> Allow { get; init; } = [];

    /// <summary>该模式下额外拒绝的工具列表</summary>
    [JsonPropertyName("deny")]
    public List<string> Deny { get; init; } = [];
}

/// <summary>
/// Hook 配置项 — 对齐 TS 版 HookSchema
/// </summary>
public sealed class HookSettings
{
    /// <summary>Hook 类型（如 command）</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Hook 执行命令</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>工具名匹配器（正则或通配符）</summary>
    [JsonPropertyName("matcher")]
    public string? Matcher { get; init; }

    /// <summary>超时时间（毫秒）</summary>
    [JsonPropertyName("timeout")]
    public int? Timeout { get; init; }

    /// <summary>执行期间显示的状态消息</summary>
    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; init; }

    /// <summary>是否仅执行一次</summary>
    [JsonPropertyName("once")]
    public bool? Once { get; init; }
}

/// <summary>
/// MCP 服务器配置 — 对齐 TS 版 McpServerConfig
/// </summary>
public sealed class McpServerSettings
{
    /// <summary>服务器类型（stdio/sse/http）</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>启动命令（stdio 类型时使用）</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>启动命令参数列表</summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; init; } = [];

    /// <summary>HTTP/SSE 端点 URL</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>环境变量注入</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; init; } = [];

    /// <summary>HTTP 请求头注入</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; init; } = [];
}

/// <summary>
/// 沙箱配置 — 对齐 TS 版 SandboxSettingsSchema
/// </summary>
public sealed class SandboxSettings
{
    /// <summary>是否启用沙箱</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>沙箱模式</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    /// <summary>沙箱镜像名</summary>
    [JsonPropertyName("image")]
    public string? Image { get; init; }

    /// <summary>允许访问的路径白名单</summary>
    [JsonPropertyName("allowedPaths")]
    public List<string> AllowedPaths { get; init; } = [];

    /// <summary>是否限制网络访问</summary>
    [JsonPropertyName("restrictNetwork")]
    public bool? RestrictNetwork { get; init; }

    /// <summary>内存上限（MB）</summary>
    [JsonPropertyName("memoryLimitMb")]
    public int? MemoryLimitMb { get; init; }
}

/// <summary>
/// 插件配置项 — 对齐 TS 版 PluginSettings
/// </summary>
public sealed class PluginSettings
{
    /// <summary>是否启用该插件</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>插件自定义配置键值对</summary>
    [JsonPropertyName("config")]
    public Dictionary<string, string> Config { get; init; } = [];
}

/// <summary>
/// Worktree 配置 — 对齐 TS 版 worktree
/// </summary>
public sealed class WorktreeSettings
{
    /// <summary>需要符号链接到新 worktree 的目录列表</summary>
    [JsonPropertyName("symlinkDirectories")]
    public List<string> SymlinkDirectories { get; init; } = [];

    /// <summary>稀疏检出的路径列表</summary>
    [JsonPropertyName("sparsePaths")]
    public List<string> SparsePaths { get; init; } = [];
}

/// <summary>
/// 活跃 Worktree 会话 — 对齐 TS 版 activeWorktreeSession，持久化到 settings.local.json
/// </summary>
public sealed class ActiveWorktreeSessionJson
{
    /// <summary>原始工作目录</summary>
    [JsonPropertyName("originalCwd")]
    public string? OriginalCwd { get; init; }

    /// <summary>worktree 路径</summary>
    [JsonPropertyName("worktreePath")]
    public string? WorktreePath { get; init; }

    /// <summary>worktree 名称</summary>
    [JsonPropertyName("worktreeName")]
    public string? WorktreeName { get; init; }

    /// <summary>worktree 分支名</summary>
    [JsonPropertyName("worktreeBranch")]
    public string? WorktreeBranch { get; init; }

    /// <summary>原始分支名</summary>
    [JsonPropertyName("originalBranch")]
    public string? OriginalBranch { get; init; }

    /// <summary>原始 HEAD 提交哈希</summary>
    [JsonPropertyName("originalHeadCommit")]
    public string? OriginalHeadCommit { get; init; }

    /// <summary>会话 ID</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    /// <summary>是否基于 Hook 创建</summary>
    [JsonPropertyName("hookBased")]
    public bool? HookBased { get; init; }

    /// <summary>创建耗时（毫秒）</summary>
    [JsonPropertyName("creationDurationMs")]
    public long? CreationDurationMs { get; init; }

    /// <summary>是否使用了稀疏检出路径</summary>
    [JsonPropertyName("usedSparsePaths")]
    public bool? UsedSparsePaths { get; init; }
}

/// <summary>
/// 状态栏配置 — 对齐 TS 版 statusLine
/// </summary>
public sealed class StatusLineSettings
{
    /// <summary>状态栏类型（如 command）</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>状态栏执行命令</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>状态栏内边距</summary>
    [JsonPropertyName("padding")]
    public int? Padding { get; init; }
}

/// <summary>
/// 工具评分配置 — 对齐 CS 版 ToolScoreSettings
/// </summary>
public sealed class ToolScoreSettingsJson
{
    /// <summary>工具成功时的评分增量</summary>
    [JsonPropertyName("successDelta")]
    public int? SuccessDelta { get; init; }

    /// <summary>工具失败时的评分扣减量</summary>
    [JsonPropertyName("failDelta")]
    public int? FailDelta { get; init; }

    /// <summary>工具告警阈值分数</summary>
    [JsonPropertyName("warningThreshold")]
    public int? WarningThreshold { get; init; }

    /// <summary>评分下限</summary>
    [JsonPropertyName("scoreMin")]
    public int? ScoreMin { get; init; }

    /// <summary>评分上限</summary>
    [JsonPropertyName("scoreMax")]
    public int? ScoreMax { get; init; }

    /// <summary>每小时评分衰减率</summary>
    [JsonPropertyName("decayRatePerHour")]
    public double? DecayRatePerHour { get; init; }

    /// <summary>衰减后恢复到的基准分数</summary>
    [JsonPropertyName("decayRecoveryScore")]
    public int? DecayRecoveryScore { get; init; }
}

/// <summary>
/// 供应商预设 — vendor 字典的值，命名的供应商/模型/端点组合
/// </summary>
public sealed class ProfileSettings
{
    /// <summary>供应商名称（如 openai/sensenova/agnes/deepseek/anthropic）</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    /// <summary>协议类型（openai-compatible / anthropic / azure）— 决定 API 格式和认证方式</summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; init; }

    /// <summary>首选模型 ID（如 gpt-4o、deepseek-v4-flash）— 该供应商的默认模型</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>API 端点（如 https://token.sensenova.cn/v1）</summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    /// <summary>API Key 环境变量名（如 OPENAI_API_KEY、ANTHROPIC_API_KEY）</summary>
    [JsonPropertyName("apiKeyEnvVar")]
    public string? ApiKeyEnvVar { get; init; }

    /// <summary>该供应商可用的模型列表 — 配置大于内置，GUI 下拉由此驱动</summary>
    [JsonPropertyName("models")]
    public List<ModelItemConfig> Models { get; init; } = [];

    /// <summary>模型列表拉取端点相对路径（如 "models"、"v1/models"）— 拼接 endpoint 得到完整 URL。null 表示不拉取。</summary>
    [JsonPropertyName("modelsEndpoint")]
    public string? ModelsEndpoint { get; init; }
}
