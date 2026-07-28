namespace Core.Utils;

public sealed class ExponentialBackoff
{
    public TimeSpan BaseDelay { get; }
    public TimeSpan MaxDelay { get; }
    public int MaxShiftBits { get; }

    public static ExponentialBackoff Default { get; } = new(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(30), 5);

    public ExponentialBackoff(TimeSpan baseDelay, TimeSpan maxDelay, int maxShiftBits = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDelay, baseDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxShiftBits, 1);

        BaseDelay = baseDelay;
        MaxDelay = maxDelay;
        MaxShiftBits = maxShiftBits;
    }

    public TimeSpan CalculateDelay(int retryCount)
    {
        var shift = Math.Min(retryCount, MaxShiftBits);
        var ms = Math.Min(BaseDelay.TotalMilliseconds * (1 << shift), MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }
}
