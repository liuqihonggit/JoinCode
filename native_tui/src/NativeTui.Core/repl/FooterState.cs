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

    /// <summary>右侧通知（行 2 右对齐 dim，如 "◐ medium · /effort"；非空时 footer 占 2 行）。
    /// 静态回退层 — 动态队列 <see cref="Notifications"/> 未接线或无当前显示时生效。</summary>
    public string? Notification { get; init; }

    /// <summary>
    /// 上下文余量告警文本（M8-7 TASK064 P1 — 行 2 右对齐，色由 ContextWarningLevel 决定；null = 无告警）。
    /// 独立槽位于 Notification：通知是临时态（effort 12s 超时/transcript 提示，ReplShell 覆盖管理），
    /// 告警是持续态（直到余量回升 &gt;25% 或 /compact）— 互不覆盖；共存时告警优先渲染。
    /// 数据源 context_usage 事件（引擎权威 percentLeft）— 与 Tokens tracker 的累计口径互补。
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

    /// <summary>动态通知队列（行 2 右侧优先消费 <see cref="ReplNotifications.Current"/> —
    /// 队列状态机由渲染帧 Tick 推进；null = 未接线回退静态 <see cref="Notification"/>）。
    /// 聚合决策：record 增持有状态组件引用而非 FooterBar 持字段 — FooterState 保持
    /// 渲染状态投影单一通道（FooterBar 无状态可多实例），Driver/Shell 以 with 表达式挂载；
    /// FooterState 无相等性消费方（全部替换式更新），引用字段不影响既有语义。</summary>
    public ReplNotifications? Notifications { get; init; }

    /// <summary>Token 用量跟踪（footer 右侧 TokenWarning 渲染源 — Driver 契约
    /// <see cref="TokenUsageTracker.RecordUsage"/> 接线；null = 未接线不渲染）。</summary>
    public TokenUsageTracker? Tokens { get; init; }

    /// <summary>
    /// vim 模式指示（TASK066 板块 E 连线 E3 — 左侧 INSERT 指示渲染源）：INSERT 态 "-- INSERT --"
    ///（dim）、NORMAL 态 null 不渲染（基准 PromptInputFooterLeftSide.tsx:170/191 —
    /// isVimModeEnabled() &amp;&amp; vimMode === "INSERT" 才显示，NORMAL 无指示）；
    /// null 亦涵盖 vim 关闭（构造默认 — 零渲染变化）。
    /// </summary>
    public string? VimMode { get; init; }
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
