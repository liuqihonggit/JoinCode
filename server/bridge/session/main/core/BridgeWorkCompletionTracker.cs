namespace Core.Bridge;

/// <summary>
/// Bridge 工作完成跟踪器 — 跟踪已完成的工作 ID
/// </summary>
internal sealed class BridgeWorkCompletionTracker {
    private ImmutableDictionary<string, byte> _completed = ImmutableDictionary<string, byte>.Empty;

    /// <summary>是否已完成指定工作项</summary>
    public bool IsCompleted(string workId) => Volatile.Read(ref _completed).ContainsKey(workId);

    /// <summary>标记工作项已完成</summary>
    public void Mark(string workId) => ImmutableInterlocked.Update(ref _completed, d => d.ContainsKey(workId) ? d : d.Add(workId, (byte)0));

    /// <summary>清空所有记录</summary>
    public void Clear() => Volatile.Write(ref _completed, ImmutableDictionary<string, byte>.Empty);
}