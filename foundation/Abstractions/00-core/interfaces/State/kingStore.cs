namespace JoinCode.Abstractions.Interfaces;

public sealed class ThinkingEntry
{
    public string SessionId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? ModelId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public interface IThinkingStore
{
    Task StoreAsync(string sessionId, string content, string? modelId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThinkingEntry>> GetRecentAsync(string sessionId, int count, CancellationToken cancellationToken = default);

    Task<ThinkingEntry?> GetLatestAsync(string sessionId, CancellationToken cancellationToken = default);

    Task ClearAsync(string sessionId, CancellationToken cancellationToken = default);
}
