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

    /// <summary>
    /// OSC 21337 tab 状态指示器（基准 ink/hooks/use-tab-status.ts — 终端侧栏彩点 + 短状态文本）。
    /// 字段省略（null）= 不发送，终端保持原值；rgb 为 0xRRGGBB 打包值，输出小写十六进制零填充。
    /// status 文本转义对齐基准：先 `\`→`\\` 再 `;`→`\;`。
    /// </summary>
    /// <param name="indicatorRgb">指示点颜色（null = 省略字段）。</param>
    /// <param name="status">状态文本（null = 省略字段）。</param>
    /// <param name="statusRgb">状态文本颜色（null = 省略字段）。</param>
    public static string TabStatus(int? indicatorRgb = null, string? status = null, int? statusRgb = null)
    {
        var parts = new List<string>(3);
        if (indicatorRgb is { } indicator)
        {
            parts.Add($"indicator={RgbHex(indicator)}");
        }

        if (status is { } text)
        {
            parts.Add($"status={text.Replace("\\", "\\\\").Replace(";", "\\;")}");
        }

        if (statusRgb is { } color)
        {
            parts.Add($"status-color={RgbHex(color)}");
        }

        return $"\x1b]21337;{string.Join(";", parts)}\x07";
    }

    /// <summary>清空 OSC 21337 全部三字段（基准 CLEAR_TAB_STATUS — 退出时清残留指示点）。</summary>
    public static string ClearTabStatus() => "\x1b]21337;indicator=;status=;status-color=\x07";

    /// <summary>rgb 打包值 → `#rrggbb` 小写十六进制零填充（基准 termio/osc.ts rgb() padStart(2,'0')）。</summary>
    private static string RgbHex(int rgb) => $"#{rgb:x6}";
}
