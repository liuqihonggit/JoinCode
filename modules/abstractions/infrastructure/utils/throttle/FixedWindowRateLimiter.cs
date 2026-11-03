namespace Core.Utils;

/// <summary>
/// 固定窗口限流器 — 在固定时间窗口内限制最大请求数
/// </summary>
public sealed class FixedWindowRateLimiter {
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private WindowState _state = new(0, DateTime.UtcNow);

    private sealed record WindowState(int Count, DateTime WindowStart);

    /// <summary>
    /// 构造固定窗口限流器
    /// </summary>
    /// <param name="maxRequests">窗口内最大请求数</param>
    /// <param name="window">时间窗口长度</param>
    public FixedWindowRateLimiter(int maxRequests, TimeSpan window) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequests);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        _maxRequests = maxRequests;
        _window = window;
    }

    /// <summary>
    /// 尝试获取一个请求配额
    /// </summary>
    /// <returns>窗口内未达上限返回 true，否则 false</returns>
    public bool TryAcquire() {
        while (true) {
            var current = Volatile.Read(ref _state);
            var now = DateTime.UtcNow;
            var count = current.Count;
            var windowStart = current.WindowStart;

            if (now - windowStart >= _window) {
                windowStart = now;
                count = 0;
            }

            if (count >= _maxRequests)
                return false;

            var newState = new WindowState(count + 1, windowStart);
            if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                return true;
        }
    }

    /// <summary>
    /// 重置限流器，清空当前窗口计数并重新开始计时
    /// </summary>
    public void Reset() {
        Interlocked.Exchange(ref _state, new WindowState(0, DateTime.UtcNow));
    }
}
