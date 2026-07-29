namespace JoinCode.Abstractions.Interfaces;

public sealed record RateLimitSnapshot
{
    public int? RequestLimit { get; init; }
    public int? RequestRemaining { get; init; }
    public DateTime? RequestResetsAt { get; init; }
    public int? TokenLimit { get; init; }
    public int? TokenRemaining { get; init; }
    public DateTime? TokenResetsAt { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;
}

public interface IRateLimitTracker
{
    void Update(RateLimitSnapshot snapshot);

    RateLimitSnapshot? GetLatestSnapshot();

    void Clear();
}
