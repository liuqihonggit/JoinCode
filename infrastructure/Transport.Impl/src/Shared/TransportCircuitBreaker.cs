namespace JoinCode.Transport;

public sealed class TransportCircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _coolDownPeriod;
    private int _consecutiveFailures;
    private DateTimeOffset _openedAt;
    private CircuitBreakerState _state = CircuitBreakerState.Closed;

    public CircuitBreakerState State => GetCurrentState();
    public bool IsOpen => State == CircuitBreakerState.Open;
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);
    public int FailureThreshold => _failureThreshold;
    public TimeSpan CoolDownPeriod => _coolDownPeriod;
    public DateTimeOffset? OpenedAt => _state != CircuitBreakerState.Closed ? _openedAt : null;

    public TransportCircuitBreaker(int failureThreshold = 3, int coolDownMs = 30000)
    {
        if (failureThreshold < 1) throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        if (coolDownMs < 1) throw new ArgumentOutOfRangeException(nameof(coolDownMs));

        _failureThreshold = failureThreshold;
        _coolDownPeriod = TimeSpan.FromMilliseconds(coolDownMs);
    }

    public void RecordSuccess()
    {
        Volatile.Write(ref _consecutiveFailures, 0);
        _state = CircuitBreakerState.Closed;
    }

    public void RecordFailure()
    {
        Interlocked.Increment(ref _consecutiveFailures);
        if (_consecutiveFailures >= _failureThreshold && _state != CircuitBreakerState.Open)
        {
            _state = CircuitBreakerState.Open;
            _openedAt = DateTimeOffset.UtcNow;
        }
    }

    public bool TryProbe()
    {
        var currentState = GetCurrentState();
        if (currentState == CircuitBreakerState.Closed) return true;
        if (currentState == CircuitBreakerState.HalfOpen) return true;
        if (currentState == CircuitBreakerState.Open &&
            DateTimeOffset.UtcNow - _openedAt >= _coolDownPeriod)
        {
            _state = CircuitBreakerState.HalfOpen;
            return true;
        }
        return false;
    }

    private CircuitBreakerState GetCurrentState()
    {
        if (_state == CircuitBreakerState.Open &&
            DateTimeOffset.UtcNow - _openedAt >= _coolDownPeriod)
        {
            return CircuitBreakerState.HalfOpen;
        }
        return _state;
    }
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen,
}
