namespace JoinCode.Hands.Desktop;

/// <summary>
/// Win32 环境感知服务 — 弹窗检测/光标状态/异步等待（PRD E-01/E-03）
/// </summary>
[Register(typeof(IEnvironmentAwarenessService), ServiceLifetime.Singleton)]
public sealed partial class Win32EnvironmentAwarenessService : ServiceEntity, IEnvironmentAwarenessService
{
    private static readonly IntPtr ArrowCursor = CursorNativeMethods.LoadCursor(IntPtr.Zero, CursorNativeMethods.IdcArrow);
    private static readonly IntPtr WaitCursor = CursorNativeMethods.LoadCursor(IntPtr.Zero, CursorNativeMethods.IdcWait);
    private static readonly IntPtr AppStartingCursor = CursorNativeMethods.LoadCursor(IntPtr.Zero, CursorNativeMethods.IdcAppstarting);
    private static readonly IntPtr HelpCursor = CursorNativeMethods.LoadCursor(IntPtr.Zero, CursorNativeMethods.IdcHelp);

    private static readonly FrozenSet<string> DecisionKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "确认", "保存", "覆盖", "删除", "替换", "是否");

    private static readonly FrozenSet<string> RetryableKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "错误", "失败", "超时", "重试", "无法");

    /// <summary>检测当前是否有非预期弹窗（E-01）</summary>
    public Task<PopupInfo?> DetectPopupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var foreground = User32NativeMethods.GetForegroundWindow();
        var popups = new List<(IntPtr Handle, string Title)>();

        User32NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (hWnd == foreground)
                return true;
            if (!User32NativeMethods.IsWindowVisible(hWnd))
                return true;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(title))
                return true;

            if (User32NativeMethods.GetWindowRect(hWnd, out var rect) && rect.Width < 600 && rect.Height < 400)
                popups.Add((hWnd, title));

            return true;
        }, IntPtr.Zero);

        if (popups.Count == 0)
            return Task.FromResult<PopupInfo?>(null);

        var (handle, popupTitle) = popups[0];
        var category = ClassifyPopup(popupTitle);
        return Task.FromResult<PopupInfo?>(new PopupInfo(handle, popupTitle, null, category));
    }

    /// <summary>获取当前光标状态（E-03）</summary>
    public Task<CursorState> GetCursorStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ci = new CursorNativeMethods.CursorInfo
        {
            cbSize = Marshal.SizeOf<CursorNativeMethods.CursorInfo>()
        };

        if (!CursorNativeMethods.GetCursorInfo(ref ci) || (ci.flags & CursorNativeMethods.CursorShowing) == 0)
            return Task.FromResult(CursorState.Unknown);

        if (ci.hCursor == WaitCursor)
            return Task.FromResult(CursorState.Wait);
        if (ci.hCursor == AppStartingCursor)
            return Task.FromResult(CursorState.AppStarting);
        if (ci.hCursor == HelpCursor)
            return Task.FromResult(CursorState.Help);
        if (ci.hCursor == ArrowCursor)
            return Task.FromResult(CursorState.Normal);

        return Task.FromResult(CursorState.Unknown);
    }

    /// <summary>等待异步操作完成（E-03）— 光标恢复 Normal 或超时</summary>
    public async Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = await GetCursorStateAsync(cancellationToken).ConfigureAwait(false);
            if (state is CursorState.Normal or CursorState.Help)
                return true;

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    internal static PopupCategory ClassifyPopup(string title)
    {
        foreach (var kw in DecisionKeywords)
        {
            if (title.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return PopupCategory.NeedsDecision;
        }

        foreach (var kw in RetryableKeywords)
        {
            if (title.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return PopupCategory.Retryable;
        }

        return PopupCategory.Closeable;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var length = User32NativeMethods.GetWindowTextLength(hWnd);
        if (length == 0)
            return string.Empty;

        var sb = new StringBuilder(length + 1);
        User32NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
