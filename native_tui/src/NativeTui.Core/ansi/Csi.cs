namespace NativeTui.Core.Ansi;

/// <summary>
/// CSI（Control Sequence Introducer）转义序列 — 光标移动/擦除/滚动区/同步输出。
/// 期望值对齐基准源码 src/ink/termio/csi.ts 与 log-update.ts 的 DECSTBM 硬件滚动。
/// 计数为 1 时按 VT 规范省略参数。
/// </summary>
public static class Csi
{
    /// <summary>光标定位（1 起始行列，原点省略参数）。</summary>
    public static string CursorPosition(int row, int col) =>
        row == 1 && col == 1 ? "\x1b[H" : $"\x1b[{row};{col}H";

    /// <summary>光标上移 n 行。</summary>
    public static string CursorUp(int n) => n <= 1 ? "\x1b[A" : $"\x1b[{n}A";

    /// <summary>光标下移 n 行。</summary>
    public static string CursorDown(int n) => n <= 1 ? "\x1b[B" : $"\x1b[{n}B";

    /// <summary>光标右移 n 列。</summary>
    public static string CursorForward(int n) => n <= 1 ? "\x1b[C" : $"\x1b[{n}C";

    /// <summary>光标左移 n 列。</summary>
    public static string CursorBackward(int n) => n <= 1 ? "\x1b[D" : $"\x1b[{n}D";

    /// <summary>全屏擦除。</summary>
    public static string EraseScreen() => "\x1b[2J";

    /// <summary>清滚动缓冲区（scrollback — CSI 3J，/clear 清对话历史滚入区）。</summary>
    public static string EraseSavedLines() => "\x1b[3J";

    /// <summary>擦除光标到行尾。</summary>
    public static string EraseLineToEnd() => "\x1b[0K";

    /// <summary>整行擦除（inline 收缩清底部行）。</summary>
    public static string EraseLine() => "\x1b[2K";

    /// <summary>设置滚动区（DECSTBM，基准用于 ScrollBox 硬件滚动优化）。</summary>
    public static string SetScrollRegion(int top, int bottom) => $"\x1b[{top};{bottom}r";

    /// <summary>滚动区内上滚 n 行。</summary>
    public static string ScrollUp(int n) => n <= 1 ? "\x1b[S" : $"\x1b[{n}S";

    /// <summary>滚动区内下滚 n 行。</summary>
    public static string ScrollDown(int n) => n <= 1 ? "\x1b[T" : $"\x1b[{n}T";

    /// <summary>DEC 2026 同步输出开始 — 整帧原子上屏消除闪烁。</summary>
    public static string SynchronizedOutputBegin() => DecPrivateMode.Enable(DecPrivateModeKind.SynchronizedOutput);

    /// <summary>DEC 2026 同步输出结束。</summary>
    public static string SynchronizedOutputEnd() => DecPrivateMode.Disable(DecPrivateModeKind.SynchronizedOutput);
}
