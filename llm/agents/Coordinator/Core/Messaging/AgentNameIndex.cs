namespace Core.Agents.Coordinator.Core.Messaging;

/// <summary>
/// 子代理名称索引 — 多键映射 name→agentId，O(1) 查找
/// 注册键: agentId、Name、Task(description)、DisplayName（均大小写不敏感）
/// 注销时仅移除属于该 agentId 的键（同名子代理不误删）
/// </summary>
internal sealed class AgentNameIndex {
    private volatile ImmutableDictionary<string, string> _index = ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册子代理的多个名称键到 agentId
    /// </summary>
    internal void Register(string agentId, string name, string task, string? displayName) {
        SetIndex(agentId, agentId);
        if (!string.IsNullOrEmpty(name))
            SetIndex(name, agentId);
        if (!string.IsNullOrEmpty(task))
            SetIndex(task, agentId);
        if (!string.IsNullOrEmpty(displayName))
            SetIndex(displayName, agentId);
    }

    /// <summary>
    /// 注销子代理的名称键 — 仅移除属于该 agentId 的键
    /// </summary>
    internal void Unregister(string agentId, string name, string task, string? displayName) {
        TryRemoveIndexIfMatch(agentId, agentId);
        if (!string.IsNullOrEmpty(name))
            TryRemoveIndexIfMatch(name, agentId);
        if (!string.IsNullOrEmpty(task))
            TryRemoveIndexIfMatch(task, agentId);
        if (!string.IsNullOrEmpty(displayName))
            TryRemoveIndexIfMatch(displayName, agentId);
    }

    /// <summary>
    /// 按名称查找 agentId — O(1) 字典查找
    /// </summary>
    internal string? Find(string name) {
        return _index.TryGetValue(name, out var agentId) ? agentId : null;
    }

    private void SetIndex(string key, string value) {
        var current = _index;
        while (true) {
            var updated = current.SetItem(key, value);
            if (Interlocked.CompareExchange(ref _index, updated, current) == current) return;
            current = _index;
        }
    }

    private bool TryRemoveIndexIfMatch(string key, string expectedValue) {
        var current = _index;
        while (current.TryGetValue(key, out var existing) && string.Equals(existing, expectedValue, StringComparison.Ordinal)) {
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _index, updated, current) == current) return true;
            current = _index;
        }
        return false;
    }
}