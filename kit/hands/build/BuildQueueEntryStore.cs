namespace Services.Build;

/// <summary>
/// 编译队列条目存储 — 成对管理 <see cref="BuildQueueEntry"/> 与其等待句柄，
/// 保证同一 BuildId 在两个字典中的生命周期一致（同时添加、同时移除）。
/// </summary>
internal sealed class BuildQueueEntryStore {
    private readonly ConcurrentDictionary<string, BuildQueueEntry> _entries = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BuildQueueResult>> _waitHandles = new();

    /// <summary>
    /// 已注册条目数（含已完成但未移除的）。
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 所有条目的快照拷贝 — 用于状态查询。
    /// </summary>
    public BuildQueueEntry[] GetAllEntries() => _entries.Values.ToArray();

    /// <summary>
    /// 成对添加条目与等待句柄。同 BuildId 重复添加会覆盖。
    /// </summary>
    public void Add(string buildId, BuildQueueEntry entry, TaskCompletionSource<BuildQueueResult> tcs) {
        _entries[buildId] = entry;
        _waitHandles[buildId] = tcs;
    }

    /// <summary>
    /// 查找条目。
    /// </summary>
    public bool TryGetEntry(string buildId, out BuildQueueEntry entry)
        => _entries.TryGetValue(buildId, out entry!);

    /// <summary>
    /// 查找等待句柄。
    /// </summary>
    public bool TryGetTcs(string buildId, [MaybeNullWhen(false)] out TaskCompletionSource<BuildQueueResult> tcs)
        => _waitHandles.TryGetValue(buildId, out tcs);

    /// <summary>
    /// 仅移除等待句柄（条目保留用于历史查询）。
    /// </summary>
    public bool TryRemoveTcs(string buildId, [MaybeNullWhen(false)] out TaskCompletionSource<BuildQueueResult> tcs)
        => _waitHandles.TryRemove(buildId, out tcs);

    /// <summary>
    /// 取消所有未完成的等待句柄 — 用于 Dispose 时通知等待者。
    /// </summary>
    public void CancelAll() {
        foreach (var tcs in _waitHandles.Values)
            tcs.TrySetCanceled();
    }
}