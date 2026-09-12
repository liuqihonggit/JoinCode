namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 非托管内存资源表 — 明确登记,卸载时逐个释放
/// <para>插件持有非托管内存(SafeHandle 包装),登记在此表</para>
/// <para>卸载时 ReleaseAll 逐个释放 SafeHandle,确保无泄漏</para>
/// </summary>
public sealed class UnmanagedResourceTable
{
    private readonly ConcurrentDictionary<string, UnmanagedResourceEntry> _resources = new();

    /// <summary>登记非托管资源 — 返回句柄,Dispose 时自动注销</summary>
    public UnmanagedResourceHandle Register(string key, SafeHandle handle, long estimatedBytes)
    {
        var entry = new UnmanagedResourceEntry(key, handle, estimatedBytes);
        _resources[key] = entry;
        return new UnmanagedResourceHandle(this, key);
    }

    /// <summary>获取所有已登记的非托管资源</summary>
    public IReadOnlyCollection<UnmanagedResourceEntry> GetAll() => _resources.Values.ToList();

    /// <summary>获取指定资源</summary>
    public bool TryGet(string key, [NotNullWhen(true)] out UnmanagedResourceEntry? entry) => _resources.TryGetValue(key, out entry);

    /// <summary>释放所有非托管资源 — 卸载时调用</summary>
    public void ReleaseAll()
    {
        foreach (var entry in _resources.Values)
        {
            if (!entry.Handle.IsClosed)
                entry.Handle.Dispose();
        }
        _resources.Clear();
    }

    /// <summary>当前资源数量</summary>
    public int Count => _resources.Count;

    /// <summary>估计总字节数</summary>
    public long TotalEstimatedBytes => _resources.Values.Sum(e => e.EstimatedBytes);

    /// <summary>内部释放 — UnmanagedResourceHandle.Dispose 调用</summary>
    internal void ReleaseInternal(string key)
    {
        if (_resources.TryRemove(key, out var entry))
        {
            if (!entry.Handle.IsClosed)
                entry.Handle.Dispose();
        }
    }
}
