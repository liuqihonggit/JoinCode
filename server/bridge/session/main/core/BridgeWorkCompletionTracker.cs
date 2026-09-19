namespace Core.Bridge;

/// <summary>
/// Bridge 工作完成跟踪器 — 跟踪已完成的工作 ID
/// </summary>
internal sealed class BridgeWorkCompletionTracker {
    private readonly ConcurrentDictionary<string, byte> _completed = new();

    /// <summary>是否已完成指定工作项</summary>
    public bool IsCompleted(string workId) => _completed.ContainsKey(workId);

    /// <summary>标记工作项已完成</summary>
    public void Mark(string workId) => _completed.TryAdd(workId, 0);

    /// <summary>清空所有记录</summary>
    public void Clear() => _completed.Clear();
}