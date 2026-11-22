namespace JoinCode.Abstractions.Interfaces;

public sealed record RateLimitSnapshot {
    /// <summary>获取请求限制数。</summary>
    public int? RequestLimit { get; init; }
    /// <summary>获取剩余请求数。</summary>
    public int? RequestRemaining { get; init; }
    /// <summary>获取请求限制重置时间。</summary>
    public DateTime? RequestResetsAt { get; init; }
    /// <summary>获取令牌限制数。</summary>
    public int? TokenLimit { get; init; }
    /// <summary>获取剩余令牌数。</summary>
    public int? TokenRemaining { get; init; }
    /// <summary>获取令牌限制重置时间。</summary>
    public DateTime? TokenResetsAt { get; init; }
    /// <summary>获取快照捕获时间。</summary>
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;
}

public interface IRateLimitTracker {
    /// <summary>更新速率限制快照。</summary>
    /// <param name="snapshot">快照数据。</param>
    void Update(RateLimitSnapshot snapshot);

    /// <summary>获取最新快照。</summary>
    RateLimitSnapshot? GetLatestSnapshot();

    /// <summary>清除所有快照。</summary>
    void Clear();

    /// <summary>
    /// 注册快照更新处理器 — 当 Update 被调用时触发（TASK073 Phase 3 — 速率限制事件桥接）
    /// </summary>
    /// <param name="handler">处理器，接收更新后的 RateLimitSnapshot</param>
    void RegisterUpdateHandler(Action<RateLimitSnapshot> handler);

    /// <summary>
    /// 注销快照更新处理器
    /// </summary>
    /// <param name="handler">要注销的处理器</param>
    void UnregisterUpdateHandler(Action<RateLimitSnapshot> handler);
}