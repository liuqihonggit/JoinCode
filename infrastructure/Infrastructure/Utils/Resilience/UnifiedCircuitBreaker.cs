namespace Infrastructure.Utils.Resilience;

public enum CircuitBreakerPhase
{
    Closed,
    Open,
    HalfOpen
}

public sealed class UnifiedCircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly int _halfOpenMaxProbe;
    private readonly object _lock = new();

    private CircuitBreakerPhase _phase = CircuitBreakerPhase.Closed;
    private int _consecutiveFailures;
    private int _totalFailures;
    private int _totalSuccesses;
    private int _halfOpenProbeCount;
    private DateTimeOffset _openedAt = DateTimeOffset.MinValue;

    public string Name { get; }

    public CircuitBreakerPhase Phase
    {
        get
        {
            lock (_lock)
            {
                return GetCurrentPhase();
            }
        }
    }

    public int ConsecutiveFailures
    {
        get { lock (_lock) { return _consecutiveFailures; } }
    }

    public int TotalFailures
    {
        get { lock (_lock) { return _totalFailures; } }
    }

    public int TotalSuccesses
    {
        get { lock (_lock) { return _totalSuccesses; } }
    }

    public DateTimeOffset? OpenedAt
    {
        get
        {
            lock (_lock)
            {
                return _phase != CircuitBreakerPhase.Closed ? _openedAt : null;
            }
        }
    }

    public bool IsOpen => Phase == CircuitBreakerPhase.Open;

    public UnifiedCircuitBreaker(string name, int failureThreshold = 5, TimeSpan? openDuration = null, int halfOpenMaxProbe = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureThreshold);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfOpenMaxProbe);

        Name = name;
        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _halfOpenMaxProbe = halfOpenMaxProbe;
    }

    public UnifiedCircuitBreaker(string name, CircuitBreakerConfig config)
        : this(name, config.FailureThreshold, config.OpenDuration, config.HalfOpenMaxProbe)
    {
    }

    public bool TryProbe()
    {
        lock (_lock)
        {
            var currentPhase = GetCurrentPhase();

            switch (currentPhase)
            {
                case CircuitBreakerPhase.Closed:
                    return true;

                case CircuitBreakerPhase.HalfOpen:
                    if (_halfOpenProbeCount < _halfOpenMaxProbe)
                    {
                        _halfOpenProbeCount++;
                        return true;
                    }
                    return false;

                case CircuitBreakerPhase.Open:
                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _totalSuccesses++;
            _halfOpenProbeCount = 0;
            _phase = CircuitBreakerPhase.Closed;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            _totalFailures++;

            if (_phase == CircuitBreakerPhase.HalfOpen)
            {
                _phase = CircuitBreakerPhase.Open;
                _openedAt = DateTimeOffset.UtcNow;
                _halfOpenProbeCount = 0;
                return;
            }

            if (_consecutiveFailures >= _failureThreshold && _phase != CircuitBreakerPhase.Open)
            {
                _phase = CircuitBreakerPhase.Open;
                _openedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _phase = CircuitBreakerPhase.Closed;
            _consecutiveFailures = 0;
            _halfOpenProbeCount = 0;
            _openedAt = DateTimeOffset.MinValue;
        }
    }

    private CircuitBreakerPhase GetCurrentPhase()
    {
        if (_phase == CircuitBreakerPhase.Open &&
            DateTimeOffset.UtcNow - _openedAt >= _openDuration)
        {
            _phase = CircuitBreakerPhase.HalfOpen;
            _halfOpenProbeCount = 0;
        }

        return _phase;
    }
}
