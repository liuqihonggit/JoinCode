namespace NativeTui.Core.Ansi;

/// <summary>
/// SGR（Select Graphic Rendition）转义序列 — 文本样式与颜色。
/// 期望值对齐基准源码 src/ink/termio/sgr.ts 与 src/ink/screen.ts 的 SGR 常量
/// （BOLD_CODE 关闭用 22 而非 21、UNDERLINE 关闭用 24 等）。
/// </summary>
public static class Sgr
{
    /// <summary>全部样式重置。</summary>
    public const string Reset = "\x1b[0m";

    /// <summary>粗体开。</summary>
    public const string Bold = "\x1b[1m";

    /// <summary>暗淡（dim）开。</summary>
    public const string Dim = "\x1b[2m";

    /// <summary>斜体开。</summary>
    public const string Italic = "\x1b[3m";

    /// <summary>下划线开。</summary>
    public const string Underline = "\x1b[4m";

    /// <summary>反色（前景背景互换）开。</summary>
    public const string Inverse = "\x1b[7m";

    /// <summary>删除线开。</summary>
    public const string Strikethrough = "\x1b[9m";

    /// <summary>粗体/暗淡关（基准用 22 同时关两者，不用 21）。</summary>
    public const string ResetBold = "\x1b[22m";

    /// <summary>下划线关。</summary>
    public const string ResetUnderline = "\x1b[24m";

    /// <summary>反色关。</summary>
    public const string ResetInverse = "\x1b[27m";

    /// <summary>前景色恢复终端默认。</summary>
    public const string DefaultForeground = "\x1b[39m";

    /// <summary>背景色恢复终端默认。</summary>
    public const string DefaultBackground = "\x1b[49m";

    /// <summary>真彩前景色。</summary>
    public static string Foreground(int r, int g, int b) => $"\x1b[38;2;{r};{g};{b}m";

    /// <summary>真彩背景色。</summary>
    public static string Background(int r, int g, int b) => $"\x1b[48;2;{r};{g};{b}m";

    /// <summary>ANSI 256 色索引前景色。</summary>
    public static string ForegroundAnsi256(int index) => $"\x1b[38;5;{index}m";

    /// <summary>ANSI 256 色索引背景色。</summary>
    public static string BackgroundAnsi256(int index) => $"\x1b[48;5;{index}m";
}
