namespace NativeTui.Core.Repl;

/// <summary>权限模式 — 对齐基准 utils/permissions/PermissionMode.ts 的有限集合。</summary>
public enum PermissionMode : byte
{
    /// <summary>默认模式（footer 无模式指示，仅 ? for shortcuts）。</summary>
    Default = 0,

    /// <summary>计划模式 ⏸（sage 绿）。</summary>
    Plan = 1,

    /// <summary>自动接受编辑 ⏵⏵（电紫）。</summary>
    AcceptEdits = 2,

    /// <summary>绕过权限 ⏵⏵（红）。</summary>
    BypassPermissions = 3,

    /// <summary>不再询问 ⏵⏵（红）。</summary>
    DontAsk = 4,

    /// <summary>自动模式 ⏵⏵（warning 琥珀，基准 TRANSCRIPT_CLASSIFIER 条件条目）。</summary>
    Auto = 5,
}

/// <summary>
/// 权限模式配置 — 对齐基准 PERMISSION_MODE_CONFIG（符号/标题/色键）。
/// 标题直接存小写显示值（基准 title.toLowerCase() 后的最终形态）。
/// </summary>
public static class PermissionModeInfo
{
    /// <summary>模式符号（plan ⏸，其余激活模式 ⏵⏵，default 空）。</summary>
    public static string Symbol(PermissionMode mode) => mode switch
    {
        PermissionMode.Plan => "⏸",
        PermissionMode.AcceptEdits or PermissionMode.BypassPermissions or PermissionMode.DontAsk or PermissionMode.Auto => "⏵⏵",
        _ => string.Empty,
    };

    /// <summary>模式小写标题。</summary>
    public static string Title(PermissionMode mode) => mode switch
    {
        PermissionMode.Plan => "plan mode",
        PermissionMode.AcceptEdits => "accept edits",
        PermissionMode.BypassPermissions => "bypass permissions",
        PermissionMode.DontAsk => "don't ask",
        PermissionMode.Auto => "auto mode",
        _ => "default",
    };

    /// <summary>模式主题色（plan→planMode，acceptEdits→autoAccept，auto→warning，bypass/dontAsk→error）。</summary>
    public static Color ColorOf(PermissionMode mode, Theme theme) => mode switch
    {
        PermissionMode.Plan => theme.PlanMode,
        PermissionMode.AcceptEdits => theme.AutoAccept,
        PermissionMode.Auto => theme.Warning,
        PermissionMode.BypassPermissions or PermissionMode.DontAsk => theme.Error,
        _ => theme.Text,
    };
}

/// <summary>
/// 权限模式循环器 — 对齐基准 getNextPermissionMode.ts（Shift+Tab carousel，非 ant 链）：
/// default → acceptEdits → plan → bypassPermissions → auto → default；
/// bypass/auto 按可用性短路（不可用即跳过），任何未知/未来模式回 default。
/// </summary>
public static class PermissionModeCycler
{
    /// <summary>计算下一个权限模式（bypass/auto 可用性开关对齐基准 isBypassPermissionsModeAvailable / canCycleToAuto）。</summary>
    public static PermissionMode Next(PermissionMode current, bool bypassAvailable, bool autoAvailable) => current switch
    {
        PermissionMode.Default => PermissionMode.AcceptEdits,
        PermissionMode.AcceptEdits => PermissionMode.Plan,
        PermissionMode.Plan => bypassAvailable ? PermissionMode.BypassPermissions
            : autoAvailable ? PermissionMode.Auto
            : PermissionMode.Default,
        PermissionMode.BypassPermissions => autoAvailable ? PermissionMode.Auto : PermissionMode.Default,
        PermissionMode.DontAsk => PermissionMode.Default,
        _ => PermissionMode.Default,
    };
}
