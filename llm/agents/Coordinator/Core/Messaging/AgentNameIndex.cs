namespace Core.Agents.Coordinator.Core.Messaging;

/// <summary>
/// 子代理名称索引 — 多键映射 name→agentId，O(1) 查找
/// 注册键: agentId、Name、Task(description)、DisplayName（均大小写不敏感）
/// 注销时仅移除属于该 agentId 的键（同名子代理不误删）
/// </summary>
internal sealed class AgentNameIndex
{
    private readonly ConcurrentDictionary<string, string> _index = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册子代理的多个名称键到 agentId
    /// </summary>
    internal void Register(string agentId, string name, string task, string? displayName)
    {
        _index[agentId] = agentId;
        if (!string.IsNullOrEmpty(name))
            _index[name] = agentId;
        if (!string.IsNullOrEmpty(task))
            _index[task] = agentId;
        if (!string.IsNullOrEmpty(displayName))
            _index[displayName] = agentId;
    }

    /// <summary>
    /// 注销子代理的名称键 — 仅移除属于该 agentId 的键
    /// </summary>
    internal void Unregister(string agentId, string name, string task, string? displayName)
    {
        _index.TryRemove(new KeyValuePair<string, string>(agentId, agentId));
        if (!string.IsNullOrEmpty(name))
            _index.TryRemove(new KeyValuePair<string, string>(name, agentId));
        if (!string.IsNullOrEmpty(task))
            _index.TryRemove(new KeyValuePair<string, string>(task, agentId));
        if (!string.IsNullOrEmpty(displayName))
            _index.TryRemove(new KeyValuePair<string, string>(displayName, agentId));
    }

    /// <summary>
    /// 按名称查找 agentId — O(1) 字典查找
    /// </summary>
    internal string? Find(string name)
    {
        return _index.TryGetValue(name, out var agentId) ? agentId : null;
    }
}
