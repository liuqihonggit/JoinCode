namespace NativeTui.Core.Terminal;

/// <summary>
/// 终端视口位置探测器（TASK077 — inline 模式 SGR 鼠标坐标 ↔ Screen 内容坐标对齐）。
/// inline 模式下应用首帧从终端当前光标行起画（非视口顶部），Screen row 0 对应终端
/// scrollback 某行 R（首帧光标行）。SGR 鼠标坐标是终端视口坐标（视口顶 = 0），
/// HitTest 注册的 frame.Y 是 Screen 内容坐标（Screen row 0 = 0）。两者偏移：
/// <code>
/// contentTopScrollbackRow = cursorScrollbackRow - contentCursorRow
/// viewportOffset = contentTopScrollbackRow - viewportTopScrollbackRow
/// screenRow = sgrRow - viewportOffset
/// </code>
/// 仅 Windows（Linux 终端无 Win32 API；跨平台后续用 CSI 6n 光标查询 + 推算视口顶）。
/// </summary>
public sealed class TerminalViewportProbe
{
    private readonly IntPtr _stdout;

    /// <summary>创建探测器 — 获取 stdout 句柄用于 GetConsoleScreenBufferInfo。</summary>
    public TerminalViewportProbe()
    {
        _stdout = GetStdHandle(StdOutputHandle);
    }

    /// <summary>
    /// 查询当前终端视口偏移量（Screen row 0 对应的终端视口行号）。
    /// contentCursorRow = 上一帧渲染尾光标在 Screen 中的行号（ReplShell._cursor.Row）。
    /// 返回值 = Screen row 0 对应终端视口行；SGR row 减去此值 = Screen row。
    /// 查询失败返回 0（零偏移降级 — 视口模式或非交互终端不偏移）。
    /// </summary>
    public int QueryViewportOffset(int contentCursorRow)
    {
        if (!GetConsoleScreenBufferInfo(_stdout, out var info))
        {
            return 0;
        }

        return ComputeViewportOffset(info.dwCursorPosition.Y, info.srWindowTop, contentCursorRow);
    }

    /// <summary>
    /// 纯数学：计算视口偏移量（Screen row 0 对应的终端视口行号）。
    /// cursorScrollbackRow = 终端光标在 scrollback 中的行号；
    /// viewportTopScrollbackRow = 终端视口顶部在 scrollback 中的行号；
    /// contentCursorRow = 渲染尾光标在 Screen 中的行号（_cursor.Row）。
    /// </summary>
    internal static int ComputeViewportOffset(int cursorScrollbackRow, int viewportTopScrollbackRow, int contentCursorRow)
    {
        var contentTopScrollbackRow = cursorScrollbackRow - contentCursorRow;
        return contentTopScrollbackRow - viewportTopScrollbackRow;
    }

    /// <summary>
    /// 纯数学：SGR 终端视口行 → Screen 内容行。
    /// sgrRow = SGR 鼠标 0-based 视口行；viewportOffset = <see cref="ComputeViewportOffset"/> 返回值。
    /// </summary>
    internal static int ScreenRowFromSgr(int sgrRow, int viewportOffset) => sgrRow - viewportOffset;

    private const int StdOutputHandle = -11;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SmallRect
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ConsoleScreenBufferInfo
    {
        public Coord dwSize;
        public Coord dwCursorPosition;
        public ushort wAttributes;
        public SmallRect srWindow;
        public Coord dwMaximumWindowSize;

        public int srWindowTop => srWindow.Top;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out ConsoleScreenBufferInfo lpConsoleScreenBufferInfo);
}
