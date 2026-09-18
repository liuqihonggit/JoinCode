namespace McpToolRegistry;

/// <summary>
/// 远程工具规格缓存 — 管理每个客户端最近一次同步的工具规格列表
/// 持有以 clientId 为 key 的规格缓存字典，提供查询、更新、移除、清空操作
/// </summary>
internal sealed class RemoteToolSpecCache
{
    private readonly ConcurrentDictionary<string, List<ToolSpec>> _specs = new();

    /// <summary>获取客户端的工具规格缓存（未找到返回 null）</summary>
    public List<ToolSpec>? GetSpecs(string clientId)
        => _specs.TryGetValue(clientId, out var specs) ? specs : null;

    /// <summary>更新客户端的工具规格缓存</summary>
    public void Update(string clientId, List<ToolSpec> specs)
        => _specs[clientId] = specs;

    /// <summary>移除客户端的规格缓存</summary>
    public void Remove(string clientId)
        => _specs.TryRemove(clientId, out _);

    /// <summary>清空所有缓存</summary>
    public void Clear() => _specs.Clear();
}
