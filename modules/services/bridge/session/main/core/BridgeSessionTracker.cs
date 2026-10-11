namespace Core.Bridge;

/// <summary>
/// Bridge 会话状态跟踪器 — 组合根，聚合三个职责单一的小类
/// 对齐 TS 端 runBridgeLoop 中的 Map/Set 集合
/// </summary>
internal sealed class BridgeSessionTracker {
    /// <summary>会话注册表 — 管理所有以 sessionId 为 key 的会话状态</summary>
    public BridgeSessionRegistry Sessions { get; } = new();

    /// <summary>工作完成跟踪器 — 管理已完成的工作 ID</summary>
    public BridgeWorkCompletionTracker WorkCompletion { get; } = new();

    /// <summary>会话标题跟踪器 — 管理已获取标题的会话（按兼容 ID 索引）</summary>
    public BridgeSessionTitleTracker Titles { get; } = new();

    /// <summary>清理单个会话的跟踪状态 — 跨 Sessions 和 Titles 协作</summary>
    public void CleanupSession(string sessionId, Action<string>? onRemoveCompatId = null) {
        if (Sessions.TryGetCompatId(sessionId, out var compatId) && compatId is not null) {
            Titles.Remove(compatId);
            onRemoveCompatId?.Invoke(compatId);
        }
        Sessions.Remove(sessionId);
    }

    /// <summary>清理所有跟踪状态</summary>
    public void ClearAll() {
        Sessions.Clear();
        WorkCompletion.Clear();
        Titles.Clear();
    }
}