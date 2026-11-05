namespace Tools.Handlers;

/// <summary>
/// Agent 工具中间件接口 — 拦截和转换 Agent 创建流程
/// 继承通用 Task 中间件接口，复用管道构建和异常捕获机制
/// 执行顺序由 Order 属性决定（管道构建处 OrderBy 排序），数值小的先执行
/// </summary>
public interface IAgentToolMiddleware : IMiddleware<AgentToolContext> {
    /// <summary>
    /// 执行优先级 — 数值小的先执行
    /// </summary>
    int Order { get; }
}