namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面操作安全检查器 — 撤销元意识（PRD U-01/U-02/U-04）的生产实现
/// 维护危险坐标区域集合 + 窗口未保存数据启发式检测
/// </summary>
[Register(typeof(IDesktopSafetyChecker), ServiceLifetime.Singleton)]
public sealed partial class DesktopSafetyChecker : ServiceEntity, IDesktopSafetyChecker
{
    private readonly List<DangerousZone> _zones = new();
    private readonly AsyncLock _lock = new("DesktopSafetyChecker");

    private static readonly FrozenSet<string> UnsavedKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "未保存", "unsaved", "modified");

    /// <summary>检查鼠标点击坐标是否命中危险区域（U-04）</summary>
    public Task<UnsafeOperationKind> CheckClickAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (_lock.TryLock() ?? throw new System.TimeoutException("锁等待超时"))
        {
            foreach (var zone in _zones)
            {
                if (x >= zone.X && x <= zone.X + zone.Width &&
                    y >= zone.Y && y <= zone.Y + zone.Height)
                    return Task.FromResult(UnsafeOperationKind.DangerousCoordinate);
            }
        }

        return Task.FromResult(UnsafeOperationKind.None);
    }

    /// <summary>检查关闭窗口是否可能导致未保存数据丢失（U-01/U-02）</summary>
    public Task<UnsafeOperationKind> CheckWindowCloseAsync(IntPtr hWnd, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var title = GetWindowTitle(hWnd);
        if (string.IsNullOrEmpty(title))
            return Task.FromResult(UnsafeOperationKind.None);

        if (title.StartsWith("*", StringComparison.Ordinal))
            return Task.FromResult(UnsafeOperationKind.WindowClose);

        foreach (var kw in UnsavedKeywords)
        {
            if (title.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(UnsafeOperationKind.WindowClose);
        }

        return Task.FromResult(UnsafeOperationKind.None);
    }

    /// <summary>注册危险坐标区域 — 如通过视觉识别到"确定删除"按钮时调用（U-04）</summary>
    public void RegisterDangerousZone(int x, int y, int width, int height)
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException("锁等待超时"))
        {
            _zones.Add(new DangerousZone(x, y, width, height));
        }
    }

    /// <summary>清空危险区域集合</summary>
    public void ClearDangerousZones()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException("锁等待超时"))
        {
            _zones.Clear();
        }
    }

    /// <summary>当前已注册的危险区域数量</summary>
    public int DangerousZoneCount
    {
        get
        {
            using (_lock.TryLock() ?? throw new System.TimeoutException("锁等待超时"))
            {
                return _zones.Count;
            }
        }
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

    private readonly record struct DangerousZone(int X, int Y, int Width, int Height);

    protected override void OnDispose()
    {
    }
}
