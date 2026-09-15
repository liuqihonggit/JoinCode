namespace JoinCode.Hands.Desktop;

/// <summary>
/// Win32 窗口震动服务 — 通过 <c>MoveWindow</c> 震动控制台窗口，<c>FlashWindowEx</c> 闪烁任务栏。
/// </summary>
[Register(typeof(IWindowShakeService), ServiceLifetime.Singleton)]
public sealed class Win32WindowShakeService : ServiceEntity, IWindowShakeService
{
    private static readonly int[] s_offsets = { -20, 20, -16, 16, -12, 12, -8, 8, -4, 4, 0 };
    private const int StepMs = 80;
    private const int MaxParentTraversal = 16;

    private readonly ILogger<Win32WindowShakeService>? _logger;

    /// <summary>
    /// 构造 Win32 窗口震动服务。
    /// </summary>
    /// <param name="logger">日志记录器（可选）。</param>
    public Win32WindowShakeService(ILogger<Win32WindowShakeService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 震动当前进程的控制台窗口 — X 轴阻尼偏移动画 + 任务栏闪烁，总时长约 880ms。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ShakeWindowAsync(CancellationToken cancellationToken = default)
    {
        var hwnd = ResolveShakeableWindow();
        if (hwnd == IntPtr.Zero)
        {
            _logger?.LogWarning("无法找到可震动的窗口句柄");
            return;
        }

        FlashTaskbarCore(hwnd);

        if (!User32NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            _logger?.LogWarning("GetWindowRect 失败，无法震动");
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        for (var i = 0; i < s_offsets.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            User32NativeMethods.MoveWindow(hwnd, rect.Left + s_offsets[i], rect.Top, width, height, true);
            await Task.Delay(StepMs, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 闪烁任务栏图标 — 通过 <c>FlashWindowEx</c> 闪烁 5 次。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task FlashTaskbarAsync(CancellationToken cancellationToken = default)
    {
        var hwnd = ResolveShakeableWindow();
        if (hwnd == IntPtr.Zero)
        {
            _logger?.LogWarning("无法找到可闪烁的窗口句柄");
            return Task.CompletedTask;
        }

        FlashTaskbarCore(hwnd);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 解析可震动的窗口句柄 — 从控制台窗口开始向上遍历父窗口链，
    /// 找到第一个可见且能设置焦点的顶层窗口。
    /// </summary>
    private IntPtr ResolveShakeableWindow()
    {
        var hwnd = PulseNativeMethods.GetConsoleWindow();
        if (hwnd == IntPtr.Zero)
        {
            _logger?.LogWarning("GetConsoleWindow 返回零句柄");
            return IntPtr.Zero;
        }

        var bestVisible = IntPtr.Zero;
        var current = hwnd;
        for (var i = 0; i < MaxParentTraversal && current != IntPtr.Zero; i++)
        {
            if (IsWindowShakeable(current))
            {
                if (User32NativeMethods.SetForegroundWindow(current))
                {
                    _logger?.LogInformation("找到可设置焦点的窗口句柄 {Hwnd}（向上遍历 {Depth} 层）", current, i);
                    return current;
                }

                if (bestVisible == IntPtr.Zero)
                {
                    bestVisible = current;
                }
            }

            current = User32NativeMethods.GetParent(current);
        }

        if (bestVisible != IntPtr.Zero)
        {
            _logger?.LogInformation("未找到可设置焦点的窗口，使用最顶层可见窗口句柄 {Hwnd}", bestVisible);
            User32NativeMethods.SetForegroundWindow(bestVisible);
            return bestVisible;
        }

        _logger?.LogWarning("向上遍历父窗口链未找到任何可见窗口，回退到原始控制台窗口句柄 {Hwnd}", hwnd);
        return hwnd;
    }

    private static bool IsWindowShakeable(IntPtr hwnd)
    {
        if (!User32NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (!User32NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return rect.Right - rect.Left > 0 && rect.Bottom - rect.Top > 0;
    }

    private void FlashTaskbarCore(IntPtr hwnd)
    {
        var fi = new FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = User32NativeMethods.FLASHW_ALL,
            uCount = 5,
            dwTimeout = 0
        };
        User32NativeMethods.FlashWindowEx(ref fi);
    }
}
