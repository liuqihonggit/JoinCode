namespace NativeTui.Core.Repl;

/// <summary>
/// 会话三态 — 对齐基准 REPL.tsx:1187 sessionStatus（waiting &gt; busy &gt; idle 优先级）：
/// waiting = 权限确认挂起/对话框开启；busy = 回合进行中；idle = 空闲。
/// 标题动画（AnimatedTerminalTitle）与 21337 tab 状态（useTabStatus）共同消费此枚举。
/// </summary>
public enum TerminalSessionStatus
{
    /// <summary>空闲（回合结束）— tab preset：绿点 #00D75F / "Idle" / 灰 #888888。</summary>
    Idle,

    /// <summary>回合进行中（isLoading）— tab preset：橙点 #FF9500 / "Working…" / 橙 #FF9500；标题动画 ⠂⠐。</summary>
    Busy,

    /// <summary>等待用户（权限确认/对话框）— tab preset：蓝点 #5F87FF / "Waiting" / 蓝 #5F87FF。</summary>
    Waiting,
}

/// <summary>
/// 会话三态 → OSC 21337 tab 状态序列（唯一数据源 — 消费方禁止散落硬编码 RGB）。
/// preset 逐色对齐基准 ink/hooks/use-tab-status.ts TAB_STATUS_PRESETS。
/// </summary>
public static class TerminalSessionStatusExtensions
{
    /// <summary>三态 → OSC 21337 序列（A5 接线：ReplShell 状态变化时写，退出 ClearTabStatus）。</summary>
    /// <param name="status">会话三态。</param>
    /// <returns>完整 21337 三字段序列。</returns>
    public static string ToTabStatusSequence(this TerminalSessionStatus status) =>
        status switch
        {
            TerminalSessionStatus.Idle => Osc.TabStatus(indicatorRgb: 0x00D75F, status: "Idle", statusRgb: 0x888888),
            TerminalSessionStatus.Busy => Osc.TabStatus(indicatorRgb: 0xFF9500, status: "Working…", statusRgb: 0xFF9500),
            TerminalSessionStatus.Waiting => Osc.TabStatus(indicatorRgb: 0x5F87FF, status: "Waiting", statusRgb: 0x5F87FF),
            _ => Osc.ClearTabStatus(),
        };
}
