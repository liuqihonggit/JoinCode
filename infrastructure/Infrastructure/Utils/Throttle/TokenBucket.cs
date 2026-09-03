namespace Core.Utils;

public sealed class TokenBucket : IDisposable
{
    private readonly AsyncLock _gate = new();
    private readonly double _capacity;
    private readonly double _refillRatePerSecond;
    private readonly Func<DateTime> _timeProvider;
    private double _tokens;
    private DateTime _lastRefillTime;

    public double CurrentTokens
    {
        get
        {
            var guard = _gate.TryLock();
            if (guard is null)
                return _tokens;

            using (guard)
            {
                Refill();
                return _tokens;
            }
        }
    }

    public TokenBucket(double capacity, double refillRatePerSecond, Func<DateTime>? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillRatePerSecond);
        _capacity = capacity;
        _refillRatePerSecond = refillRatePerSecond;
        _timeProvider = timeProvider ?? DefaultTimeProvider;
        _tokens = capacity;
        _lastRefillTime = _timeProvider();
    }

    public async Task WaitForTokensAsync(double requiredTokens, CancellationToken ct = default)
    {
        while (true)
        {
            using var guard = _gate.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

            Refill();

            if (_tokens >= requiredTokens)
            {
                _tokens -= requiredTokens;
                return;
            }
        

            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }

    public bool TryConsume(double requiredTokens)
    {
        var guard = _gate.TryLock();
        if (guard is null)
            return false;

        using (guard)
        {
            Refill();

            if (_tokens >= requiredTokens)
            {
                _tokens -= requiredTokens;
                return true;
            }

            return false;
        }
    }

    private void Refill()
    {
        var now = _timeProvider();
        var elapsedSeconds = (now - _lastRefillTime).TotalSeconds;

        if (elapsedSeconds > 0)
        {
            var tokensToAdd = elapsedSeconds * _refillRatePerSecond;
            _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
            _lastRefillTime = now;
        }
    }

    public void Dispose() => _gate.Dispose();

    private static DateTime DefaultTimeProvider() => DateTime.UtcNow;
}
