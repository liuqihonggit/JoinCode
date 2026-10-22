namespace NativeTui.Core.Terminal;

/// <summary>
/// 光标显隐生命周期 — 对齐基准 ink/termio/dec.ts HIDE_CURSOR/SHOW_CURSOR（DEC 25）：
/// TUI 运行期隐藏硬件光标（帧内自绘输入光标，双光标错位），退出恢复显示。
/// 序列构造委托 <see cref="DecPrivateMode"/>（模式码单一数据源，禁手写 \x1b[?25 转义）。
/// 接线（未决问题，ReplShell 非本域）：主循环进入前发射 <see cref="HideCursor"/>，
/// WriteExit 尾部（Sgr.Reset 之后）发射 <see cref="ShowCursor"/> — 崩溃路径由宿主
/// 全局异常兜底统一补发 Show。
/// </summary>
public static class CursorLifecycle
{
    /// <summary>隐藏光标序列（DEC 25 reset — 运行期每帧自绘光标替代硬件光标）。</summary>
    public static readonly string Hide = DecPrivateMode.Disable(DecPrivateModeKind.CursorVisibility);

    /// <summary>显示光标序列（DEC 25 set — 退出恢复终端默认光标，幂等可重发）。</summary>
    public static readonly string Show = DecPrivateMode.Enable(DecPrivateModeKind.CursorVisibility);

    /// <summary>向输出流发射隐藏光标序列（发射即 Flush — 保证上屏时序）。</summary>
    /// <param name="output">终端输出流。</param>
    public static void HideCursor(TextWriter output)
    {
        output.Write(Hide);
        output.Flush();
    }

    /// <summary>向输出流发射显示光标序列（退出恢复 — 发射即 Flush，崩溃兜底可重入）。</summary>
    /// <param name="output">终端输出流。</param>
    public static void ShowCursor(TextWriter output)
    {
        output.Write(Show);
        output.Flush();
    }
}
