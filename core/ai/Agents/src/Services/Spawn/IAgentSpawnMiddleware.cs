namespace Core.Agents;

/// <summary>
/// 子智能体初始化中间件 — 扩展 IMiddleware&lt;AgentSpawnContext&gt;
/// </summary>
public interface IAgentSpawnMiddleware : JoinCode.Abstractions.Pipeline.IMiddleware<AgentSpawnContext> { }
