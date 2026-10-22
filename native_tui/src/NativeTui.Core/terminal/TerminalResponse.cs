namespace NativeTui.Core.Terminal;

/// <summary>终端查询响应种类 — 基准 ink/parse-keypress.ts:33-60 模式定义（探测查询的应答形态）。</summary>
public enum TerminalResponseKind
{
    /// <summary>DECRPM 回复（ESC[?mode;value$y — DECRQM 查询应答，如 DEC 2026 同步输出探测）。</summary>
    Decrpm,

    /// <summary>DA1 主设备属性回复（ESC[?a;bc）。</summary>
    Da1,

    /// <summary>DA2 次设备属性回复（ESC[&gt;a;b;cc — VT520+/xterm 终端识别）。</summary>
    Da2,

    /// <summary>kitty keyboard flags 回复（ESC[?flags u — CSI ? u 查询应答）。</summary>
    KittyFlags,

    /// <summary>DSR 光标位置回复（ESC[row;col R — CSI 6 n 查询应答）。</summary>
    CursorPosition,

    /// <summary>XTVERSION 终端身份回复（OSC 0 厂商名 — 探测 SSH 环境终端类型）。</summary>
    XtVersion,

    /// <summary>OSC 通用回复（含 ST 终结变体 — 前景色/背景色等查询应答）。</summary>
    Osc,
}

/// <summary>终端查询响应（Raw=原始序列 — 审计/诊断；Numbers=数字参数段；Payload=OSC 文本段）。</summary>
public sealed record TerminalResponse(TerminalResponseKind Kind, string Raw, int[] Numbers, string? Payload);
