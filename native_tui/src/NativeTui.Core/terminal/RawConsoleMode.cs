namespace NativeTui.Core.Terminal;

/// <summary>
/// Windows raw 控制台模式 — 进入时保存 stdin/stdout 原模式并切换
/// （输入：raw 化清除行缓冲/回显/CTRL 处理 + 禁 QuickEdit；输出：启用 VT 处理），
/// Dispose 恢复原模式。非 Windows 平台抛 <see cref="PlatformNotSupportedException"/>（跨平台驱动属后续里程碑）。
/// </summary>
public sealed class RawConsoleMode : IDisposable
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;

    private readonly IntPtr _stdin;
    private readonly IntPtr _stdout;
    private readonly uint _originalInputMode;
    private readonly uint _originalOutputMode;

    /// <summary>进入 raw 模式（记录原模式用于恢复）。</summary>
    public RawConsoleMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("RawConsoleMode 目前仅实现 Windows（M6 范围），跨平台驱动属后续里程碑");
        }

        _stdin = GetStdHandle(StdInputHandle);
        _stdout = GetStdHandle(StdOutputHandle);
        if (!GetConsoleMode(_stdin, out _originalInputMode) || !GetConsoleMode(_stdout, out _originalOutputMode))
        {
            throw new InvalidOperationException("获取控制台模式失败（可能未附加到交互终端）");
        }

        if (!SetConsoleMode(_stdin, ConsoleModeMath.ToRawInput(_originalInputMode))
            || !SetConsoleMode(_stdout, ConsoleModeMath.ToVtOutput(_originalOutputMode)))
        {
            throw new InvalidOperationException("设置控制台模式失败");
        }
    }

    /// <summary>恢复进入前的原模式。</summary>
    public void Dispose()
    {
        SetConsoleMode(_stdin, _originalInputMode);
        SetConsoleMode(_stdout, _originalOutputMode);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
