namespace JoinCode.Abstractions.LLM.Chat;

public interface ISessionMetaStore : IStore
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

    /// <summary>
    /// 会话最后活跃时间的 UTC 刻度数（DateTime.UtcNow.Ticks），用于冷恢复剪裁判定
    /// 缓存是否已冷（空闲超 vendor TTL）。0 表示未知（旧文件/未写入），冷恢复保守跳过。
    /// 对齐 Reasonix Go 版 branch meta 的 UpdatedAt。
    /// </summary>
    public long UpdatedAtUtcTicks { get; init; }
}

public static class SessionMetaSerializer
{
    public static string Serialize(SessionMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        return RelaxedJsonSerializer.Serialize(meta, SessionMetaJsonContext.Default);
    }

    public static SessionMeta Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        return RelaxedJsonSerializer.Deserialize(json, SessionMetaJsonContext.Default.SessionMeta)
            ?? new SessionMeta();
    }
}
