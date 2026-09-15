namespace JoinCode.Hands.Desktop;

/// <summary>
/// Win32 窗口震动服务 — 通过 <c>MoveWindow</c> 震动控制台窗口，<c>FlashWindowEx</c> 闪烁任务栏。
/// </summary>
[Register(typeof(IWindowShakeService), ServiceLifetime.Singleton)]
public sealed class Win32WindowShakeService : ServiceEntity, IWindowShakeService
{
    private static readonly int[] s_offsets = { -8, 8, -6, 6, -4, 4, -2, 2, 0 };
    private const int StepMs = 50;

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
    /// 震动当前进程的控制台窗口 — X 轴阻尼偏移动画，总时长约 450ms。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ShakeWindowAsync(CancellationToken cancellationToken = default)
    {
        var hwnd = PulseNativeMethods.GetConsoleWindow();
        if (hwnd == IntPtr.Zero)
        {
            _logger?.LogWarning("GetConsoleWindow 返回零句柄，无法震动");
            return;
        }

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
    /// 闪烁任务栏图标 — 通过 <c>FlashWindowEx</c> 闪烁 3 次。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task FlashTaskbarAsync(CancellationToken cancellationToken = default)
    {
        var hwnd = PulseNativeMethods.GetConsoleWindow();
        if (hwnd == IntPtr.Zero)
        {
            _logger?.LogWarning("GetConsoleWindow 返回零句柄，无法闪烁");
            return Task.CompletedTask;
        }

        var fi = new FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = User32NativeMethods.FLASHW_ALL,
            uCount = 3,
            dwTimeout = 0
        };
        User32NativeMethods.FlashWindowEx(ref fi);
        return Task.CompletedTask;
    }
}
