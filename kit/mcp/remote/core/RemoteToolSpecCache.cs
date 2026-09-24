namespace McpToolRegistry;

/// <summary>
/// 远程工具规格缓存 — 管理每个客户端最近一次同步的工具规格列表
/// 持有以 clientId 为 key 的不可变字典，无锁 CAS 更新
/// </summary>
internal sealed class RemoteToolSpecCache {
    private ImmutableDictionary<string, ImmutableList<ToolSpec>> _specs = ImmutableDictionary<string, ImmutableList<ToolSpec>>.Empty;

    /// <summary>获取客户端的工具规格缓存（未找到返回 null）</summary>
    public IReadOnlyList<ToolSpec>? GetSpecs(string clientId)
        => _specs.TryGetValue(clientId, out var specs) ? specs : null;

    /// <summary>更新客户端的工具规格缓存</summary>
    public void Update(string clientId, List<ToolSpec> specs) {
        var immutableSpecs = specs.ToImmutableList();
        while (true) {
            var current = _specs;
            var updated = current.SetItem(clientId, immutableSpecs);
            if (Interlocked.CompareExchange(ref _specs, updated, current) == current) return;
        }
    }

    /// <summary>移除客户端的规格缓存</summary>
    public void Remove(string clientId) {
        while (true) {
            var current = _specs;
            if (!current.ContainsKey(clientId)) return;
            var updated = current.Remove(clientId);
            if (Interlocked.CompareExchange(ref _specs, updated, current) == current) return;
        }
    }

    /// <summary>清空所有缓存</summary>
    public void Clear()
        => Interlocked.Exchange(ref _specs, ImmutableDictionary<string, ImmutableList<ToolSpec>>.Empty);
}
