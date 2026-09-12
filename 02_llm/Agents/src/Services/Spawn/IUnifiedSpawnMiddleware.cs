namespace Core.Agents;

/// <summary>
/// 统一 Spawn 中间件接口 — 合并 IAgentSpawnMiddleware 与 IAgentSpawnCoordMiddleware
/// </summary>
public interface IUnifiedSpawnMiddleware : IMiddleware<UnifiedSpawnContext> { }
