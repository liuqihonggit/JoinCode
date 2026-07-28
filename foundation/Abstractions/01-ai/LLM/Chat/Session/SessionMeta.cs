namespace JoinCode.Abstractions.LLM.Chat;

public interface ISessionMetaStore
{
    Task<SessionMeta?> LoadAsync(string sessionId, CancellationToken cancellationToken = default);
    Task SaveAsync(string sessionId, SessionMeta meta, CancellationToken cancellationToken = default);
}

public sealed class SessionMeta
{
    public long CacheHitTokens { get; init; }
    public long CacheMissTokens { get; init; }
    public int LastPromptTokens { get; init; }
    public int TurnCount { get; init; }
    public decimal TotalCostUsd { get; init; }
}

public static class SessionMetaSerializer
{
    public static string Serialize(SessionMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        return JsonSerializer.Serialize(meta, SessionMetaJsonContext.Default.SessionMeta);
    }

    public static SessionMeta Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        return JsonSerializer.Deserialize(json, SessionMetaJsonContext.Default.SessionMeta)
            ?? new SessionMeta();
    }
}
