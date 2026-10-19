namespace NativeTui.Core.Ansi;

/// <summary>
/// OSC（Operating System Command）转义序列 — 终端标题/超链接/进度条。
/// 对齐基准源码 src/ink/termio/osc.ts（含 tmux DCS passthrough 的包裹入口在 multiplexer 层处理）。
/// </summary>
public static class Osc
{
    /// <summary>设置终端窗口标题（BEL 终止）。</summary>
    public static string SetTitle(string title) => $"\x1b]0;{title}\x07";

    /// <summary>开启 OSC 8 超链接（ST 终止）。</summary>
    public static string Hyperlink(string uri) => $"\x1b]8;;{uri}\x1b\\";

    /// <summary>关闭 OSC 8 超链接（空 URI）。</summary>
    public static string HyperlinkEnd() => "\x1b]8;;\x1b\\";

    /// <summary>
    /// OSC 9;4 任务栏进度条（state: 0=移除 1=常规 2=错误 3=不确定 4=暂停；
    /// 基准 isProgressReportingAvailable 对 Windows Terminal 排除 — 它把 9;4 当通知）。
    /// </summary>
    public static string Progress(int state, int progress) => $"\x1b]9;4;{state};{progress}\x07";
}
