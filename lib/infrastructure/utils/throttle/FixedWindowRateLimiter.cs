namespace Core.Utils;

public sealed class FixedWindowRateLimiter
{
    private readonly AsyncLock _lock = new("FixedWindowRateLimiter");
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private int _currentCount;
    private DateTime _windowStart = DateTime.UtcNow;

    public FixedWindowRateLimiter(int maxRequests, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequests);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        _maxRequests = maxRequests;
        _window = window;
    }

    public bool TryAcquire()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            var now = DateTime.UtcNow;
            if (now - _windowStart >= _window)
            {
                _windowStart = now;
                _currentCount = 0;
            }

            if (_currentCount >= _maxRequests)
                return false;

            _currentCount++;
            return true;
        }
    }

    public void Reset()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            _windowStart = DateTime.UtcNow;
            _currentCount = 0;
        }
    }
}
