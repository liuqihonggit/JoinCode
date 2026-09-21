namespace JoinCode.Abstractions.LLM.Chat;

public interface ISessionMetaStore : IStore {
    /// <summary>异步加载会话元数据。</summary>
    Task<SessionMeta?> LoadAsync(string sessionId, CancellationToken cancellationToken = default);
    /// <summary>异步保存会话元数据。</summary>
    Task SaveAsync(string sessionId, SessionMeta meta, CancellationToken cancellationToken = default);
}

public sealed class SessionMeta {
    /// <summary>获取缓存命中 Token 数。</summary>
    public long CacheHitTokens { get; init; }
    /// <summary>获取缓存未命中 Token 数。</summary>
    public long CacheMissTokens { get; init; }
    /// <summary>获取最后提示 Token 数。</summary>
    public int LastPromptTokens { get; init; }
    /// <summary>获取回合数。</summary>
    public int TurnCount { get; init; }
    /// <summary>获取总费用(美元)。</summary>
    public decimal TotalCostUsd { get; init; }

    /// <summary>
    /// 会话最后活跃时间的 UTC 刻度数（DateTime.UtcNow.Ticks），用于冷恢复剪裁判定
    /// 缓存是否已冷（空闲超 vendor TTL）。0 表示未知（旧文件/未写入），冷恢复保守跳过。
    /// 对齐 Reasonix Go 版 branch meta 的 UpdatedAt。
    /// </summary>
    public long UpdatedAtUtcTicks { get; init; }
}

public static class SessionMetaSerializer {
    /// <summary>序列化会话元数据为 JSON 字符串。</summary>
    public static string Serialize(SessionMeta meta) {
        ArgumentNullException.ThrowIfNull(meta);

        return RelaxedJsonSerializer.Serialize(meta, SessionMetaJsonContext.Default);
    }

    /// <summary>反序列化 JSON 字符串为会话元数据。</summary>
    public static SessionMeta Deserialize(string json) {
        ArgumentException.ThrowIfNullOrEmpty(json);

        return RelaxedJsonSerializer.Deserialize(json, SessionMetaJsonContext.Default.SessionMeta)
            ?? new SessionMeta();
    }
}