namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 中央注册表 — 树形组织：mainAgent → subAgents[]
/// 核心路由：subAgentMap[mainAgent.Id] 获取该主 Agent 下的所有子 Agent
/// 支持批量 LLM 循环控制（PauseAll/ResumeAll/CancelAll）
/// </summary>
public interface IAgentRegistry
{
    /// <summary>
    /// 子 Agent 路由表 — key=mainAgent.Id, value=该主 Agent 下的子 Agent 列表
    /// 用法: subAgentMap[mainAgent.Id] 得到子 Agent 集合
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<AgentDescriptor>> SubAgentMap { get; }

    /// <summary>
    /// 注册 Agent — mainAgent(IsSubAgent=false) 或 subAgent(IsSubAgent=true, ParentAgentId 指向 mainAgent)
    /// </summary>
    AgentDescriptor Register(AgentDescriptor agent);

    /// <summary>
    /// 注销 Agent 及其所有子 Agent
    /// </summary>
    bool Unregister(string agentId);

    /// <summary>
    /// 获取 Agent — 不存在返回 null
    /// </summary>
    AgentDescriptor? Get(string agentId);

    /// <summary>
    /// 获取所有 mainAgent
    /// </summary>
    IReadOnlyList<AgentDescriptor> GetMainAgents();

    /// <summary>
    /// 获取指定 mainAgent 下的子 Agent — 等价于 SubAgentMap[mainAgentId]
    /// </summary>
    IReadOnlyList<AgentDescriptor> GetSubAgents(string mainAgentId);

    /// <summary>
    /// 获取指定 Goal 下的所有 Agent
    /// </summary>
    IReadOnlyList<AgentDescriptor> GetByGoalId(string goalId);

    /// <summary>
    /// 获取指定状态的 Agent
    /// </summary>
    IReadOnlyList<AgentDescriptor> GetByStatus(AgentStatus status);

    /// <summary>
    /// 当前注册的 Agent 总数
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 清空所有注册
    /// </summary>
    void Clear();

    /// <summary>
    /// 暂停指定 mainAgent 下所有 subAgent 的 LLM 循环
    /// </summary>
    void PauseAll(string mainAgentId);

    /// <summary>
    /// 恢复指定 mainAgent 下所有 subAgent 的 LLM 循环
    /// </summary>
    void ResumeAll(string mainAgentId);

    /// <summary>
    /// 取消指定 mainAgent 下所有 subAgent 的 LLM 循环
    /// </summary>
    void CancelAll(string mainAgentId);

    /// <summary>
    /// 暂停全局所有 Agent 的 LLM 循环
    /// </summary>
    void PauseGlobal();

    /// <summary>
    /// 恢复全局所有 Agent 的 LLM 循环
    /// </summary>
    void ResumeGlobal();
}
