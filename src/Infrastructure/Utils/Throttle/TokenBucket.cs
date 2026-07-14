namespace Core.Utils;

public sealed class TokenBucket : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly double _capacity;
    private readonly double _refillRatePerSecond;
    private readonly Func<DateTime> _timeProvider;
    private double _tokens;
    private DateTime _lastRefillTime;

    public double CurrentTokens
    {
        get
        {
            if (!_gate.Wait(0))
                return _tokens;

            try
            {
                Refill();
                return _tokens;
            }
            finally
            {
                _gate.Release();
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
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Refill();

                if (_tokens >= requiredTokens)
                {
                    _tokens -= requiredTokens;
                    return;
                }
            }
            finally
            {
                _gate.Release();
            }

            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }

    public bool TryConsume(double requiredTokens)
    {
        if (!_gate.Wait(0))
            return false;

        try
        {
            Refill();

            if (_tokens >= requiredTokens)
            {
                _tokens -= requiredTokens;
                return true;
            }

            return false;
        }
        finally
        {
            _gate.Release();
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
