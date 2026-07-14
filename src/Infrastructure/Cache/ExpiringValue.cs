namespace Core.Utils;

public sealed class ExpiringValue<T>
{
    private readonly Func<T> _refresh;
    private readonly long _intervalTicks;
    private T _value;
    private long _lastRefreshTicks;

    public ExpiringValue(Func<T> refresh, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _refresh = refresh;
        _intervalTicks = interval.Ticks;
        _value = default!;
    }

    public T GetOrRefresh()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastRefreshTicks < _intervalTicks)
            return _value!;

        _value = _refresh();
        _lastRefreshTicks = now;
        return _value;
    }

    public void Invalidate() => _lastRefreshTicks = 0;
}
