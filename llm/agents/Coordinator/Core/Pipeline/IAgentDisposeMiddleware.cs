namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 释放管道中间件接口 — 派生自通用中间件基类，专用于 Agent 释放流程
/// </summary>
public interface IAgentDisposeMiddleware : IMiddleware<AgentDisposeContext> { }
