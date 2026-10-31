namespace NativeTui.Core.Terminal;

/// <summary>
/// 终端宿主探测器 — Win32 窗口类名判定真实控制台宿主（TASK079 用户环境实证）：
/// Windows 10/11 "默认终端应用"= Windows Terminal 时，直接路径启动的控制台程序 UI 被 WT 托管，
/// 但环境变量（WT_SESSION）不注入 → 环境探测失效（用户实测"UI 是 WT 显示但探测说 conhost"）；
/// 窗口类名是权威信号：CASCADIA_HOSTING_WINDOW_CLASS = WT 托管，ConsoleWindowClass = conhost。
/// 消费方：鼠标上报武装门控（conhost 不转发鼠标序列，武装双输）+ OSC 8 支持判定
/// （WT 托管即支持 OSC 8 与鼠标 — 与 env 探测互为补充，任一命中即支持）。
/// </summary>
public static class TerminalHostDetector
{
    /// <summary>Windows Terminal 托管窗口类名（Cascadia 宿主）。</summary>
    private const string WindowsTerminalClass = "CASCADIA_HOSTING_WINDOW_CLASS";

    /// <summary>当前控制台是否由 Windows Terminal 托管（GetConsoleWindow 类名权威判定）。</summary>
    public static bool IsWindowsTerminalHosting() =>
        GetConsoleWindowClassName() == WindowsTerminalClass;

    /// <summary>取控制台窗口类名（无控制台/取失败 = 空串）。</summary>
    private static string GetConsoleWindowClassName()
    {
        var hwnd = GetConsoleWindow();
        if (hwnd == System.IntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = new System.Text.StringBuilder(256);
        return GetClassNameW(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern System.IntPtr GetConsoleWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassNameW(System.IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
}
