namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 子代理名称索引接口 — 多键映射 name→agentId，O(1) 查找
/// </summary>
public interface IAgentNameIndex {
    /// <summary>注册子代理的多个名称键到 agentId</summary>
    void Register(string agentId, string name, string task, string? displayName);

    /// <summary>注销子代理的名称键 — 仅移除属于该 agentId 的键</summary>
    void Unregister(string agentId, string name, string task, string? displayName);

    /// <summary>按名称查找 agentId — O(1) 字典查找</summary>
    string? Find(string name);
}
