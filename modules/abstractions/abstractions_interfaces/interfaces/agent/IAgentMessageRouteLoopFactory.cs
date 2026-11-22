namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 消息路由循环工厂接口 — 解耦 Swarm 对 AgentMessageRouteLoop 具体类的依赖
/// </summary>
public interface IAgentMessageRouteLoopFactory {
    /// <summary>创建路由循环实例（不自动启动 — 调用方负责 Start）</summary>
    IAgentMessageRouteLoop Create(
        IMailbox broker,
        string agentId,
        Func<CoordinatorMessage, CancellationToken, Task> handler);
}
