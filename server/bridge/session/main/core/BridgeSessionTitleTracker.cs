namespace Core.Bridge;

/// <summary>
/// Bridge 会话标题跟踪器 — 跟踪已获取标题的会话（按兼容 ID 索引）
/// </summary>
internal sealed class BridgeSessionTitleTracker {
    private readonly ConcurrentDictionary<string, byte> _titled = new();

    /// <summary>是否已获取标题</summary>
    public bool Has(string compatId) => _titled.ContainsKey(compatId);

    /// <summary>标记已获取标题</summary>
    public void Mark(string compatId) => _titled.TryAdd(compatId, 0);

    /// <summary>移除标题记录</summary>
    public void Remove(string compatId) => _titled.TryRemove(compatId, out _);

    /// <summary>清空所有记录</summary>
    public void Clear() => _titled.Clear();
}