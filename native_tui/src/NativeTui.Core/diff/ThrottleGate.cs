namespace NativeTui.Core.Diff;

/// <summary>
/// 渲染节流闸门 — 对齐基准 ink.tsx 的 throttle(fn, 16ms, leading + trailing) 60fps 节流。
/// 时间由调用方注入（同步可测），真实定时循环由宿主 App 层驱动。
/// </summary>
public sealed class ThrottleGate
{
    private readonly long _intervalTicks;
    private long _lastEnterTicks;
    private bool _hasEntered;
    private bool _pending;

    /// <summary>创建指定间隔的节流闸门。</summary>
    public ThrottleGate(int intervalMs)
    {
        _intervalTicks = TimeSpan.FromMilliseconds(intervalMs).Ticks;
    }

    /// <summary>间隔内存在被拒绝的调用，到点后应补一次（trailing 边沿）。</summary>
    public bool HasPending => _pending;

    /// <summary>在给定时刻尝试进入。首次或间隔已过则放行并清除 pending，否则拒绝并标记 pending。</summary>
    public bool TryEnter(TimeSpan now)
    {
        if (!_hasEntered || now.Ticks - _lastEnterTicks >= _intervalTicks)
        {
            _hasEntered = true;
            _lastEnterTicks = now.Ticks;
            _pending = false;
            return true;
        }

        _pending = true;
        return false;
    }
}
