namespace IO.Services;

/// <summary>
/// 终端焦点状态 — 对齐 TS terminal-focus-state.ts
/// </summary>
public enum TerminalFocusState
{
    /// <summary>终端处于焦点</summary>
    Focused,
    /// <summary>终端失去焦点</summary>
    Blurred,
    /// <summary>未知（终端不支持焦点报告，视为 Focused）</summary>
    Unknown,
}

/// <summary>
/// 终端焦点检测器 — 跨平台检测终端窗口是否处于焦点
/// 对齐 TS terminal-focus-state.ts 的 DECSET 1004 focus event 机制
/// 
/// 策略：
/// - 默认 Unknown（视为 Focused，不节流 tick）
/// - 可通过 SetFocused/SetBlurred 手动设置（如从终端转义序列回调）
/// - Windows 可选：GetForegroundWindow 检测（需要 P/Invoke，暂不启用）
/// - Unix：tcgetpgrp 检测（需要 P/Invoke，暂不启用）
/// </summary>
public sealed class TerminalFocusDetector
{
    private int _state = (int)TerminalFocusState.Unknown;

    /// <summary>当前焦点状态</summary>
    public TerminalFocusState State => (TerminalFocusState)Volatile.Read(ref _state);

    /// <summary>终端是否处于焦点（Unknown 视为 true，对齐 TS getTerminalFocused）</summary>
    public bool IsFocused => State != TerminalFocusState.Blurred;

    /// <summary>焦点变化事件</summary>
    public event EventHandler<TerminalFocusState>? FocusChanged;

    /// <summary>设置终端为焦点状态</summary>
    public void SetFocused()
    {
        var old = (TerminalFocusState)Interlocked.Exchange(ref _state, (int)TerminalFocusState.Focused);
        if (old != TerminalFocusState.Focused)
        {
            FocusChanged?.Invoke(this, TerminalFocusState.Focused);
        }
    }

    /// <summary>设置终端为失焦状态</summary>
    public void SetBlurred()
    {
        var old = (TerminalFocusState)Interlocked.Exchange(ref _state, (int)TerminalFocusState.Blurred);
        if (old != TerminalFocusState.Blurred)
        {
            FocusChanged?.Invoke(this, TerminalFocusState.Blurred);
        }
    }

    /// <summary>重置为未知状态</summary>
    public void Reset()
    {
        var old = (TerminalFocusState)Interlocked.Exchange(ref _state, (int)TerminalFocusState.Unknown);
        if (old != TerminalFocusState.Unknown)
        {
            FocusChanged?.Invoke(this, TerminalFocusState.Unknown);
        }
    }
}
