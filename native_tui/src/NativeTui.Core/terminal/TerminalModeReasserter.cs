namespace NativeTui.Core.Terminal;

/// <summary>
/// stdin 长静默 gap 检测器 — 基准 App.tsx:30-35 STDIN_RESUME_GAP_MS=5000 + :359-368 gap 检测：
/// 捕捉 tmux detach→attach、ssh 重连、休眠唤醒场景 — 终端侧 DEC 模式已被外部重置时
/// 自愈重发（调用方收到 ShouldReassert=true 时重发终端模式启用序列，如 bracketed paste）。
/// 纯状态机（时钟由调用方注入 tick — 测试确定性）；打点=MarkInputReceived（每次输入刷新静默计时）。
/// </summary>
public sealed class TerminalModeReasserter(long gapMs)
{
    /// <summary>是否已收到过输入（未武装不触发 — 启动期无输入不误发）。</summary>
    public bool IsArmed { get; private set; }

    /// <summary>输入到达打点（刷新静默计时 — 连续输入永不触发重断言）。</summary>
    public void MarkInputReceived(long nowTick)
    {
        IsArmed = true;
        _lastInputTick = nowTick;
    }

    /// <summary>静默是否超 gap（true = 调用方应重发终端模式启用序列；触发后计时重置 —
    /// 持续静默每 gap 周期触发一次，直到输入恢复）。</summary>
    public bool ShouldReassert(long nowTick)
    {
        if (!IsArmed || nowTick - _lastInputTick <= gapMs)
        {
            return false;
        }

        _lastInputTick = nowTick;
        return true;
    }

    private long _lastInputTick;
}
