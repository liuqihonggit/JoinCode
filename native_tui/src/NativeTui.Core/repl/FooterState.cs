namespace NativeTui.Core.Repl;

/// <summary>
/// footer 渲染状态 — 聚合基准 PromptInputFooter Props 的可渲染子集：
/// 左侧模式指示（PromptInputFooterLeftSide）+ agents 导航提示（v2.1.303 实测 `← for agents`，
/// 本地基准快照无此字面量，数据源为用户终端第一手文本）
/// + 右侧登录错误（Notifications apiKeyStatus missing，error 色）
/// + 右侧通知（如 effort `◐ medium · /effort`，基准 12s 超时通知，dim）。
/// </summary>
public sealed record FooterState
{
    /// <summary>权限模式（左侧模式指示；默认 Auto = 引擎标准模式 — 无模式指示）。</summary>
    public PermissionMode Mode { get; init; } = PermissionMode.Auto;

    /// <summary>是否忙（输入 ❯ dim + 左侧追加 dim esc to interrupt）。</summary>
    public bool Busy { get; init; }

    /// <summary>agents 导航提示（非 busy 时以 ` · ` 追加在左侧模式串后，dim）。</summary>
    public string? AgentsHint { get; init; }

    /// <summary>右侧登录错误（右对齐 error 色，如 "Not logged in · Run /login"）。</summary>
    public string? LoginError { get; init; }

    /// <summary>右侧通知（行 2 右对齐 dim，如 "◐ medium · /effort"；非空时 footer 占 2 行）。</summary>
    public string? Notification { get; init; }

    /// <summary>
    /// 上下文余量告警文本（M8-7 TASK064 P1 — 行 2 右对齐，色由 ContextWarningLevel 决定；null = 无告警）。
    /// 独立槽位于 Notification：通知是临时态（effort 12s 超时/transcript 提示，ReplShell 覆盖管理），
    /// 告警是持续态（直到余量回升 &gt;25% 或 /compact）— 互不覆盖；共存时告警优先渲染。
    /// </summary>
    public string? ContextWarning { get; init; }

    /// <summary>上下文余量告警级别（决定 ContextWarning 渲染色 — None 时不渲染）。</summary>
    public ContextAlertLevel ContextWarningLevel { get; init; }

    /// <summary>
    /// percentLeft → 告警形态（TokenWarning.tsx:166-177 汉化）：≤10% 告急（error 色）/ ≤25% 不足（warning 色）/
    /// &gt;25% 清空（基准 auto-compact 常态显示 N% 简化为告警区才显示 — 防噪音）。
    /// </summary>
    public static (string? Text, ContextAlertLevel Level) ResolveContextWarning(int percentLeft) => percentLeft switch
    {
        <= 10 => ($"上下文告急 ({percentLeft}%) · 运行 /compact", ContextAlertLevel.Critical),
        <= 25 => ($"上下文不足 ({percentLeft}%) · 运行 /compact", ContextAlertLevel.Warning),
        _ => (null, ContextAlertLevel.None),
    };
}

/// <summary>上下文余量告警级别（M8-7 — 三色形态选色键：告急 error / 警告 warning / 无 None）。</summary>
public enum ContextAlertLevel
{
    /// <summary>无告警（余量 &gt;25% 安全区 — 不显示防噪音）。</summary>
    None = 0,

    /// <summary>警告（余量 ≤25% — warning 色 `上下文不足 (N%) · 运行 /compact`）。</summary>
    Warning = 1,

    /// <summary>告急（余量 ≤10% — error 色 `上下文告急 (N%) · 运行 /compact`）。</summary>
    Critical = 2,
}
