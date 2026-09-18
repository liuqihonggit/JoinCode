namespace Core.Goal;


/// <summary>
/// Goal Graph 调度器接口 — 封装节点调度循环策略（轮询/事件驱动）。
/// </summary>
/// <remarks>
/// 提取自 GoalGraphEngine.ExecuteAsync 主循环，使调度策略可插拔（ADR lock-to-channel P2）。
/// 调度器只负责"取 batch → 并发执行 → 检查终止"，节点执行逻辑通过 <see cref="ProcessNodeCompletionAsync"/> 委托回调。
/// </remarks>
public interface IGraphScheduler
{
    /// <summary>
    /// 运行调度循环，直到抵达终止节点或取消。
    /// </summary>
    /// <param name="graph">目标图定义</param>
    /// <param name="context">执行上下文（持有 ReadyQueue/NodeStates 等可变状态）</param>
    /// <param name="processNodeAsync">单个节点执行+完成后逻辑的回调（由 GoalGraphEngine 提供）</param>
    /// <param name="concurrencyLimiter">并发限流器（null 表示不限流）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终目标状态决策（GoalAchieved/GoalUnmet/Continue）</returns>
    Task<NodeCompletionOutcome> RunAsync(
        GoalGraph graph,
        GraphExecutionContext context,
        ProcessNodeCompletionAsync processNodeAsync,
        AsyncLock? concurrencyLimiter,
        CancellationToken ct);
}


/// <summary>
/// 单个节点执行 + 完成后逻辑的委托（失败回退 / 终止判断 / 后继入队 / EndNode 判断）。
/// </summary>
/// <param name="nodeId">节点 ID</param>
/// <param name="dagNode">DAG 节点</param>
/// <param name="context">执行上下文</param>
/// <param name="ct">取消令牌</param>
/// <returns>节点完成后的整体目标状态决策</returns>
public delegate Task<NodeCompletionOutcome> ProcessNodeCompletionAsync(
    string nodeId,
    DagNode<GoalNodePayload> dagNode,
    GraphExecutionContext context,
    CancellationToken ct);
