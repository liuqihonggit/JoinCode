namespace Tools.Handlers;

/// <summary>
/// Agent 工具中间件接口 — 拦截和转换 Agent 创建流程
/// 继承通用 Task 中间件接口，复用管道构建和异常捕获机制
/// 执行顺序由注册顺序表决定（resources/orders.yaml 的 agent_tool_middlewares 列表），
/// 数值型的 Order 成员已移除 —— 改一个数字就要把所有实现重新比对一遍才能知道先后
/// </summary>
public interface IAgentToolMiddleware : IMiddleware<AgentToolContext> {
    /// <summary>
    /// 执行位次 — 数值小的先执行。值由注册顺序表派生
    /// (<c>OrderIndex.Default.OrderOf("agent_tool_middlewares", ...)</c>),实现类不得写死数字。
    /// </summary>
    int Order { get; }
}