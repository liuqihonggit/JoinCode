namespace Tools.Handlers;

/// <summary>
/// Agent 工具中间件接口 — 拦截和转换 Agent 创建流程
/// 继承通用 Task 中间件接口，复用管道构建和异常捕获机制
/// </summary>
public interface IAgentToolMiddleware : IMiddleware<AgentToolContext> { }
