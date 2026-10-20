namespace NativeTui.Core.Repl;

/// <summary>
/// 权限模式 — 对齐 jcc 引擎 abs_guard PermissionMode 的有限集合（引擎侧唯一数据源）：
/// plan（计划：读自动批准，写需确认）/ auto（标准：按工具类型自动判断，默认）/
/// ask（询问：每个操作都需确认）/ bypass（全放行，可被 settings.json 安全闸禁用）/
/// unattended（无人值守：红灯自动执行+审计，黑灯仍拒绝，ADR 0012）。
/// 旧 ClaudeCode 值（default/acceptEdits/dontAsk/bypassPermissions）在引擎侧已废弃合并，不再使用。
/// </summary>
public enum PermissionMode : byte
{
    /// <summary>计划模式 ⏸（读自动批准，写需确认）。</summary>
    Plan = 0,

    /// <summary>标准模式（按工具类型自动判断，引擎默认 — footer 无模式指示，仅快捷键提示）。</summary>
    Auto = 1,

    /// <summary>询问模式 ⏵⏵（每个操作都需确认）。</summary>
    Ask = 2,

    /// <summary>全放行 ⏵⏵（引擎安全闸 disableBypassPermissionsMode 可禁用）。</summary>
    Bypass = 3,

    /// <summary>无人值守 ⏵⏵（红灯自动执行+审计，黑灯仍拒绝）。</summary>
    Unattended = 4,
}

/// <summary>
/// 权限模式配置 — 符号/中文标题/主题色 + 协议字符串值（bridge set_permission_mode / session_info 的
/// 唯一翻译点，值与引擎 [EnumValue] 一致：plan/auto/ask/bypass/unattended）。
/// </summary>
public static class PermissionModeInfo
{
    /// <summary>模式符号（plan ⏸，激活模式 ⏵⏵，auto 默认态无符号）。</summary>
    public static string Symbol(PermissionMode mode) => mode switch
    {
        PermissionMode.Plan => "⏸",
        PermissionMode.Ask or PermissionMode.Bypass or PermissionMode.Unattended => "⏵⏵",
        _ => string.Empty,
    };

    /// <summary>模式中文标题（界面汉化）。</summary>
    public static string Title(PermissionMode mode) => mode switch
    {
        PermissionMode.Plan => "计划模式",
        PermissionMode.Auto => "标准模式",
        PermissionMode.Ask => "询问模式",
        PermissionMode.Bypass => "全放行",
        PermissionMode.Unattended => "无人值守",
        _ => "标准模式",
    };

    /// <summary>模式主题色（plan→planMode 绿 / ask、unattended→warning 琥珀 / bypass→error 红 / auto→text）。</summary>
    public static Color ColorOf(PermissionMode mode, Theme theme) => mode switch
    {
        PermissionMode.Plan => theme.PlanMode,
        PermissionMode.Ask or PermissionMode.Unattended => theme.Warning,
        PermissionMode.Bypass => theme.Error,
        _ => theme.Text,
    };

    /// <summary>协议字符串值（bridge 传输用，与引擎 [EnumValue] 一致）。</summary>
    public static string ToValue(PermissionMode mode) => mode switch
    {
        PermissionMode.Plan => "plan",
        PermissionMode.Auto => "auto",
        PermissionMode.Ask => "ask",
        PermissionMode.Bypass => "bypass",
        PermissionMode.Unattended => "unattended",
        _ => "auto",
    };

    /// <summary>从协议字符串值解析（未知值返回 false，调用方回退 Auto）。</summary>
    public static bool TryFromValue(string? value, out PermissionMode mode)
    {
        switch (value)
        {
            case "plan":
                mode = PermissionMode.Plan;
                return true;
            case "auto":
                mode = PermissionMode.Auto;
                return true;
            case "ask":
                mode = PermissionMode.Ask;
                return true;
            case "bypass":
                mode = PermissionMode.Bypass;
                return true;
            case "unattended":
                mode = PermissionMode.Unattended;
                return true;
            default:
                mode = PermissionMode.Auto;
                return false;
        }
    }
}

/// <summary>
/// 权限模式循环器 — jcc 引擎模式集合的 Shift+Tab carousel：
/// auto → plan → ask → bypass → unattended → auto；
/// bypass 按可用性短路（引擎 settings.json 安全闸禁用时跳过），任何未知/未来模式回 auto。
/// </summary>
public static class PermissionModeCycler
{
    /// <summary>计算下一个权限模式（bypassAvailable 来自引擎 session_info 事件的 bypassAvailable 字段）。</summary>
    public static PermissionMode Next(PermissionMode current, bool bypassAvailable) => current switch
    {
        PermissionMode.Auto => PermissionMode.Plan,
        PermissionMode.Plan => PermissionMode.Ask,
        PermissionMode.Ask => bypassAvailable ? PermissionMode.Bypass : PermissionMode.Unattended,
        PermissionMode.Bypass => PermissionMode.Unattended,
        PermissionMode.Unattended => PermissionMode.Auto,
        _ => PermissionMode.Auto,
    };
}
