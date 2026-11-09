namespace Services.Api;

/// <summary>
/// API 速率限制追踪器，维护最新的速率限制快照
/// </summary>
[Register(typeof(IRateLimitTracker), ServiceLifetime.Singleton)]
public sealed partial class RateLimitTracker : ServiceEntity, IRateLimitTracker {
    private volatile RateLimitSnapshot? _snapshot;
    private Action<RateLimitSnapshot>? _updateHandler;

    /// <summary>
    /// 更新当前速率限制快照
    /// </summary>
    /// <param name="snapshot">新的速率限制快照</param>
    public void Update(RateLimitSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _updateHandler?.Invoke(snapshot);
    }

    /// <summary>
    /// 获取最新的速率限制快照
    /// </summary>
    /// <returns>最新快照；尚未更新过时返回 null</returns>
    public RateLimitSnapshot? GetLatestSnapshot() => _snapshot;

    /// <summary>
    /// 清除当前快照
    /// </summary>
    public void Clear() => _snapshot = null;

    /// <inheritdoc />
    public void RegisterUpdateHandler(Action<RateLimitSnapshot> handler) => _updateHandler += handler;

    /// <inheritdoc />
    public void UnregisterUpdateHandler(Action<RateLimitSnapshot> handler) => _updateHandler -= handler;
}