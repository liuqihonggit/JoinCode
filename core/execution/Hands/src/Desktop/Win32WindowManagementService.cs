namespace JoinCode.Hands.Desktop;

/// <summary>
/// 窗口管理服务 — Win32 EnumWindows/SetForegroundWindow/MoveWindow/PostMessage 封装
/// </summary>
[Register(typeof(IWindowManagementService), ServiceLifetime.Singleton)]
public sealed partial class Win32WindowManagementService : ServiceEntity, IWindowManagementService
{
    private readonly ILogger<Win32WindowManagementService>? _logger;

    public Win32WindowManagementService(ILogger<Win32WindowManagementService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>枚举所有可见顶层窗口</summary>
    public Task<IReadOnlyList<WindowInfo>> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        var windows = new List<WindowInfo>();
        User32NativeMethods.EnumWindows(CollectWindow, IntPtr.Zero);
        return Task.FromResult<IReadOnlyList<WindowInfo>>(windows);

        bool CollectWindow(IntPtr hWnd, IntPtr _)
        {
            if (!User32NativeMethods.IsWindowVisible(hWnd)) return true;
            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(title)) return true;
            if (!User32NativeMethods.GetWindowRect(hWnd, out var rect)) return true;
            var processName = TryGetProcessName(hWnd);
            windows.Add(new WindowInfo(
                hWnd,
                title,
                processName,
                new WindowRect(rect.Left, rect.Top, rect.Width, rect.Height),
                IsVisible: true));
            return true;
        }
    }

    /// <summary>按标题或进程名查找窗口（模糊匹配，返回第一个命中）</summary>
    public async Task<WindowInfo?> FindAsync(string titleOrProcessName, CancellationToken cancellationToken = default)
    {
        var windows = await EnumerateAsync(cancellationToken).ConfigureAwait(false);
        foreach (var w in windows)
        {
            if (MatchWindow(w, titleOrProcessName)) return w;
        }
        return null;
    }

    /// <summary>激活窗口到前台（含 Alt 键技巧解除 Windows 前台锁定）</summary>
    public Task<bool> FocusAsync(IntPtr hWnd, CancellationToken cancellationToken = default)
    {
        SendAltTap();
        var ok = User32NativeMethods.SetForegroundWindow(hWnd);
        if (!ok)
        {
            SendAltTap();
            ok = User32NativeMethods.SetForegroundWindow(hWnd);
        }
        if (!ok) _logger?.LogWarning("SetForegroundWindow 失败: hWnd={HWnd}", hWnd);
        return Task.FromResult(ok);
    }

    /// <summary>移动并调整窗口大小</summary>
    public Task<bool> MoveAsync(IntPtr hWnd, int x, int y, int width, int height, CancellationToken cancellationToken = default)
    {
        var ok = User32NativeMethods.MoveWindow(hWnd, x, y, width, height, bRepaint: true);
        if (!ok) _logger?.LogWarning("MoveWindow 失败: hWnd={HWnd}", hWnd);
        return Task.FromResult(ok);
    }

    /// <summary>关闭窗口（发送 WM_CLOSE，温和关闭）</summary>
    public Task<bool> CloseAsync(IntPtr hWnd, CancellationToken cancellationToken = default)
    {
        var result = User32NativeMethods.PostMessage(hWnd, NativeConstants.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        var ok = result != IntPtr.Zero;
        if (!ok) _logger?.LogWarning("PostMessage WM_CLOSE 失败: hWnd={HWnd}", hWnd);
        return Task.FromResult(ok);
    }

    protected override void OnDispose()
    {
    }

    // ---------- 可测试的 internal static 纯方法 ----------

    /// <summary>模糊匹配窗口标题或进程名（忽略大小写）</summary>
    internal static bool MatchWindow(WindowInfo info, string query)
    {
        if (string.IsNullOrEmpty(query)) return false;
        var comparison = StringComparison.OrdinalIgnoreCase;
        if (info.Title.Contains(query, comparison)) return true;
        if (info.ProcessName is { } processName && processName.Contains(query, comparison)) return true;
        return false;
    }

    // ---------- 私有辅助 ----------

    /// <summary>发送 Alt 键 tap 解除 Windows 前台锁定（SetForegroundWindow 限制）</summary>
    private static void SendAltTap()
    {
        var altDown = new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x12, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero } }
        };
        var altUp = new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x12, wScan = 0, dwFlags = NativeConstants.KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } }
        };
        User32NativeMethods.SendInput(1, [altDown], Marshal.SizeOf<INPUT>());
        User32NativeMethods.SendInput(1, [altUp], Marshal.SizeOf<INPUT>());
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var length = User32NativeMethods.GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        User32NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string? TryGetProcessName(IntPtr hWnd)
    {
        try
        {
            User32NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
