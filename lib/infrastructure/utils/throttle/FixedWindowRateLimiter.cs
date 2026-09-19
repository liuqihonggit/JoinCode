namespace Core.Utils;

/// <summary>
/// 固定窗口限流器 — 在固定时间窗口内限制最大请求数
/// </summary>
public sealed class FixedWindowRateLimiter {
    private readonly AsyncLock _lock = new("FixedWindowRateLimiter");
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private int _currentCount;
    private DateTime _windowStart = DateTime.UtcNow;

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
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            var now = DateTime.UtcNow;
            if (now - _windowStart >= _window) {
                _windowStart = now;
                _currentCount = 0;
            }

            if (_currentCount >= _maxRequests)
                return false;

            _currentCount++;
            return true;
        }
    }

    /// <summary>
    /// 重置限流器，清空当前窗口计数并重新开始计时
    /// </summary>
    public void Reset() {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            _windowStart = DateTime.UtcNow;
            _currentCount = 0;
        }
    }
}