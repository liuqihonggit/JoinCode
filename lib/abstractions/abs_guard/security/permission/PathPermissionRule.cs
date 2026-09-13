namespace JoinCode.Abstractions.Security;

/// <summary>
/// 路径权限规则 — 对齐 TS PermissionRule
/// 描述一条路径级的权限规则，如 "Read(/path/to/dir/**)" 或 "Edit(/secrets/**)"
/// </summary>
public sealed record PathPermissionRule
{
    /// <summary>
    /// 规则适用的工具类型 — 对齐 TS toolType: "read" | "edit"
    /// </summary>
    public PathPermissionToolType ToolType { get; init; }

    /// <summary>
    /// 规则行为 — 对齐 TS ruleBehavior: "allow" | "deny" | "ask"
    /// </summary>
    public PermissionBehavior Behavior { get; init; }

    /// <summary>
    /// 路径模式 — 对齐 TS ruleContent
    /// 格式: "/path/to/dir/**" 或 ".env" 等 glob 模式
    /// </summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>
    /// 规则来源 — 对齐 TS source
    /// </summary>
    public PathPermissionRuleSource Source { get; init; }
}

/// <summary>
/// 路径权限工具类型 — 对齐 TS toolType
/// </summary>
public enum PathPermissionToolType
{
    /// <summary>读取操作</summary>
    [EnumValue("read")] Read,

    /// <summary>编辑/写入操作</summary>
    [EnumValue("edit")] Edit
}

/// <summary>
/// 权限规则来源 — 统一 lib 层和 Host 层的规则来源枚举
/// 合并自: PermissionRuleSource (FlagSettings, PolicySettings, Command)
/// </summary>
public enum PathPermissionRuleSource
{
    /// <summary>用户设置</summary>
    [EnumValue("userSettings")] UserSettings,

    /// <summary>项目设置</summary>
    [EnumValue("projectSettings")] ProjectSettings,

    /// <summary>本地设置</summary>
    [EnumValue("localSettings")] LocalSettings,

    /// <summary>特性标志设置</summary>
    [EnumValue("flagSettings")] FlagSettings,

    /// <summary>策略设置</summary>
    [EnumValue("policySettings")] PolicySettings,

    /// <summary>命令行参数</summary>
    [EnumValue("cliArg")] CliArg,

    /// <summary>命令交互</summary>
    [EnumValue("command")] Command,

    /// <summary>会话临时规则</summary>
    [EnumValue("session")] Session
}
