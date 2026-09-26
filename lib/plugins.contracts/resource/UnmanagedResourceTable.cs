namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 非托管内存资源表 — 明确登记,卸载时逐个释放
/// <para>插件持有非托管内存(SafeHandle 包装),登记在此表</para>
/// <para>卸载时 ReleaseAll 逐个释放 SafeHandle,确保无泄漏</para>
/// </summary>
public sealed class UnmanagedResourceTable {
    private ImmutableDictionary<string, UnmanagedResourceEntry> _resources = ImmutableDictionary<string, UnmanagedResourceEntry>.Empty;

    /// <summary>登记非托管资源 — 返回句柄,Dispose 时自动注销</summary>
    public UnmanagedResourceHandle Register(string key, SafeHandle handle, long estimatedBytes) {
        var entry = new UnmanagedResourceEntry(key, handle, estimatedBytes);
        ImmutableInterlocked.Update(ref _resources, d => d.SetItem(key, entry));
        return new UnmanagedResourceHandle(this, key);
    }

    /// <summary>获取所有已登记的非托管资源</summary>
    public IReadOnlyCollection<UnmanagedResourceEntry> GetAll() => Volatile.Read(ref _resources).Values.ToList();

    /// <summary>获取指定资源</summary>
    public bool TryGet(string key, [NotNullWhen(true)] out UnmanagedResourceEntry? entry) => Volatile.Read(ref _resources).TryGetValue(key, out entry);

    /// <summary>释放所有非托管资源 — 卸载时调用</summary>
    public void ReleaseAll() {
        var snapshot = Interlocked.Exchange(ref _resources, ImmutableDictionary<string, UnmanagedResourceEntry>.Empty);
        foreach (var entry in snapshot.Values) {
            if (!entry.Handle.IsClosed)
                entry.Handle.Dispose();
        }
    }

    /// <summary>当前资源数量</summary>
    public int Count => Volatile.Read(ref _resources).Count;

    /// <summary>估计总字节数</summary>
    public long GetTotalEstimatedBytes() => Volatile.Read(ref _resources).Values.Sum(e => e.EstimatedBytes);

    /// <summary>内部释放 — UnmanagedResourceHandle.Dispose 调用</summary>
    internal void ReleaseInternal(string key) {
        UnmanagedResourceEntry? entry = null;
        ImmutableInterlocked.Update(ref _resources, d => {
            if (d.TryGetValue(key, out var e)) {
                entry = e;
                return d.Remove(key);
            }
            return d;
        });
        if (entry is { } e && !e.Handle.IsClosed)
            e.Handle.Dispose();
    }
}