namespace Core.Goal;

/// <summary>
/// 目标图变更器 — 独立类，实现 IGoalGraphMutator 接口。
/// <para>从 GoalGraphEngine 私有内部类提取，供 FunctionNodeExecutor 使用。</para>
/// <para>职责：动态添加节点/边、入队节点、添加终止节点。</para>
/// </summary>
internal sealed class GoalGraphMutator : IGoalGraphMutator
{
    private readonly GraphExecutionContext _context;
    private readonly ILogger? _logger;

    /// <summary>初始化目标图变更器</summary>
    /// <param name="context">图执行上下文</param>
    /// <param name="logger">可选日志记录器</param>
    public GoalGraphMutator(GraphExecutionContext context, ILogger? logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void AddNode(string nodeId, GoalNodePayload payload)
    {
        _context.Graph.Dag.AddNode(new DagNode<GoalNodePayload> { Id = nodeId, Payload = payload });
        _logger?.LogInformation("[GoalGraphMutator] 动态添加节点: {NodeId}", nodeId);
    }

    /// <inheritdoc/>
    public void AddEdge(string edgeId, string fromId, string toId, string? label = null)
    {
        _context.Graph.Dag.AddEdge(new DagEdge { Id = edgeId, FromId = fromId, ToId = toId, Label = label ?? string.Empty });
        _logger?.LogInformation("[GoalGraphMutator] 动态添加边: {EdgeId} ({FromId} → {ToId})", edgeId, fromId, toId);
    }

    /// <inheritdoc/>
    public void EnqueueNode(string nodeId)
    {
        _context.ReadyQueue.Enqueue(nodeId);
        _logger?.LogInformation("[GoalGraphMutator] 入队节点: {NodeId}", nodeId);
    }

    /// <inheritdoc/>
    public void AddEndNode(string nodeId)
    {
        _context.Graph.AddEndNode(nodeId);
        _logger?.LogInformation("[GoalGraphMutator] 添加终止节点: {NodeId}", nodeId);
    }
}
