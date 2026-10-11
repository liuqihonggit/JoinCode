namespace Core.Goal;


/// <summary>
/// FunctionNode 执行上下文 — 提供上游输出、全局状态、服务访问
/// </summary>
public sealed class NodeContext {
    /// <summary>当前节点 ID</summary>
    public required string NodeId { get; init; }
    /// <summary>当前节点负载</summary>
    public required GoalNodePayload CurrentNode { get; init; }
    /// <summary>上游节点输出字典（键为节点 ID，值为输出文本）</summary>
    public required IReadOnlyDictionary<string, string?> UpstreamOutputs { get; init; }
    /// <summary>全局目标状态</summary>
    public required GoalState GlobalState { get; init; }
    /// <summary>服务提供器</summary>
    public required IServiceProvider Services { get; init; }
    /// <summary>取消令牌</summary>
    public required CancellationToken CancellationToken { get; init; }
    /// <summary>图变更器，可选，用于运行时修改图结构</summary>
    public IGoalGraphMutator? GraphMutator { get; init; }
}

/// <summary>
/// 节点执行结果
/// </summary>
public sealed class NodeResult {
    /// <summary>节点输出文本</summary>
    public string? Output { get; init; }
    /// <summary>路由目标节点 ID 数组，可选</summary>
    public string[]? Routes { get; init; }
    /// <summary>附加消息，可选</summary>
    public string? Message { get; init; }
    /// <summary>已用 Token 数</summary>
    public int TokensUsed { get; init; }
    /// <summary>是否失败</summary>
    public bool IsFailed { get; init; }
    /// <summary>冲突消息列表</summary>
    public IReadOnlyList<ConflictMessage> Conflicts { get; init; } = [];

    /// <summary>
    /// 创建成功结果
    /// </summary>
    /// <param name="output">输出文本</param>
    /// <param name="tokensUsed">已用 Token 数</param>
    /// <returns>成功节点结果</returns>
    public static NodeResult Succeeded(string? output, int tokensUsed = 0)
        => new() { Output = output, TokensUsed = tokensUsed };

    /// <summary>
    /// 创建带路由的结果
    /// </summary>
    /// <param name="output">输出文本</param>
    /// <param name="routes">路由目标节点 ID 数组</param>
    /// <param name="tokensUsed">已用 Token 数</param>
    /// <returns>带路由的节点结果</returns>
    public static NodeResult Routed(string? output, string[] routes, int tokensUsed = 0)
        => new() { Output = output, Routes = routes, TokensUsed = tokensUsed };

    /// <summary>
    /// 创建失败结果
    /// </summary>
    /// <param name="errorMessage">错误消息</param>
    /// <param name="tokensUsed">已用 Token 数</param>
    /// <returns>失败节点结果</returns>
    public static NodeResult Failed(string errorMessage, int tokensUsed = 0)
        => new() { Output = null, TokensUsed = tokensUsed, Message = errorMessage, IsFailed = true };

    /// <summary>
    /// 返回附带冲突消息的新结果副本
    /// </summary>
    /// <param name="conflicts">冲突消息列表</param>
    /// <returns>附带冲突的结果副本</returns>
    public NodeResult WithConflicts(IReadOnlyList<ConflictMessage> conflicts)
        => new() { Output = Output, Routes = Routes, Message = Message, TokensUsed = TokensUsed, IsFailed = IsFailed, Conflicts = conflicts };
}