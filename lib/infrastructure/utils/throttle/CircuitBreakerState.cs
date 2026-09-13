namespace Core.Utils;

public sealed class CircuitBreakerState
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private int _consecutiveFailures;
    private DateTime _lastFailureTime = DateTime.MinValue;

    public int ConsecutiveFailures => _consecutiveFailures;
    public bool IsOpen => _consecutiveFailures >= _failureThreshold && DateTime.UtcNow - _lastFailureTime < _openDuration;

    public CircuitBreakerState(int failureThreshold, TimeSpan openDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureThreshold);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(openDuration, TimeSpan.Zero);
        _failureThreshold = failureThreshold;
        _openDuration = openDuration;
    }

    public bool ShouldTrip()
    {
        if (_consecutiveFailures >= _failureThreshold)
        {
            var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
            if (timeSinceLastFailure < _openDuration)
                return true;

            _consecutiveFailures = 0;
        }

        return false;
    }

    public void RecordSuccess() => _consecutiveFailures = 0;

    public void RecordFailure()
    {
        _consecutiveFailures++;
        _lastFailureTime = DateTime.UtcNow;
    }

    public void Reset()
    {
        _consecutiveFailures = 0;
        _lastFailureTime = DateTime.MinValue;
    }
}
