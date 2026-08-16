namespace JoinCode.Tui.Rendering;

/// <summary>
/// 终端尺寸监控器 — 钳制尺寸到合理范围，防抖处理，触发 SizeChanged 事件。
/// Terminal.Gui v2 自动处理尺寸变化重新布局，此类负责业务层面的尺寸钳制和警告。
/// </summary>
public sealed class TerminalResizeMonitor
{
    private readonly object _lock = new();
    private int _lastWidth;
    private int _lastHeight;
    private DateTime _lastChangeTime;
    private readonly int _minWidth = 80;
    private readonly int _minHeight = 24;
    private readonly int _maxWidth = 500;
    private readonly int _maxHeight = 200;
    private readonly int _debounceMs = 200;

    /// <summary>尺寸变化时触发（钳制后的 width, height）。</summary>
    public event Action<int, int>? SizeChanged;

    /// <summary>尺寸过小时触发（当前 width, height, 最小 width, 最小 height）。</summary>
    public event Action<int, int, int, int>? SizeTooSmall;

    /// <summary>创建终端尺寸监控器。</summary>
    public TerminalResizeMonitor(int initialWidth = 120, int initialHeight = 40)
    {
        (_lastWidth, _lastHeight) = Clamp(initialWidth, initialHeight);
        _lastChangeTime = DateTime.UtcNow;
    }

    /// <summary>检查并通知尺寸变化。由主循环每 100ms 调用。</summary>
    public void CheckAndNotify(int width, int height)
    {
        var (clampedW, clampedH) = Clamp(width, height);

        lock (_lock)
        {
            if (clampedW == _lastWidth && clampedH == _lastHeight) return;

            var now = DateTime.UtcNow;
            if ((now - _lastChangeTime).TotalMilliseconds < _debounceMs) return;

            _lastWidth = clampedW;
            _lastHeight = clampedH;
            _lastChangeTime = now;
        }

        if (width < _minWidth || height < _minHeight)
        {
            SizeTooSmall?.Invoke(width, height, _minWidth, _minHeight);
        }

        SizeChanged?.Invoke(clampedW, clampedH);
    }

    /// <summary>获取安全默认尺寸（120x40）。</summary>
    public static (int width, int height) GetSafeDefault() => (120, 40);

    /// <summary>钳制尺寸到合理范围。</summary>
    public (int width, int height) Clamp(int width, int height)
    {
        return (Math.Clamp(width, _minWidth, _maxWidth), Math.Clamp(height, _minHeight, _maxHeight));
    }

    /// <summary>当前尺寸是否过小。</summary>
    public bool IsTooSmall(int width, int height) => width < _minWidth || height < _minHeight;
}
