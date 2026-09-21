namespace JoinCode.Abstractions.Interfaces;

public sealed class ThinkingEntry {
    /// <summary>获取会话标识。</summary>
    public string SessionId { get; init; } = string.Empty;
    /// <summary>获取思考内容。</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>获取模型标识。</summary>
    public string? ModelId { get; init; }
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public interface IThinkingStore : IStore {
    /// <summary>异步存储思考内容。</summary>
    Task StoreAsync(string sessionId, string content, string? modelId, CancellationToken cancellationToken = default);

    /// <summary>异步获取指定会话最近的若干条思考记录。</summary>
    Task<IReadOnlyList<ThinkingEntry>> GetRecentAsync(string sessionId, int count, CancellationToken cancellationToken = default);

    /// <summary>异步获取指定会话最新的思考记录。</summary>
    Task<ThinkingEntry?> GetLatestAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>异步清空指定会话的思考记录。</summary>
    Task ClearAsync(string sessionId, CancellationToken cancellationToken = default);
}
