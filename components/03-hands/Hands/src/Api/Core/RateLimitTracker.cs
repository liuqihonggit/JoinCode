namespace Services.Api;

[Register]
public sealed partial class RateLimitTracker : IRateLimitTracker
{
    private volatile RateLimitSnapshot? _snapshot;

    public void Update(RateLimitSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
    }

    public RateLimitSnapshot? GetLatestSnapshot() => _snapshot;

    public void Clear() => _snapshot = null;
}
