
namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// 目标节点检查器 — 统一节点级检查操作：健康检查 + 循环观察 + 质量评分。
/// 对齐文档 NodeHealthChecker + QualityScorer + Observer，合并为一个接口避免类爆炸。
/// </summary>
public interface IGoalNodeInspector {
    /// <summary>
    /// 检查活跃节点的健康状况（超时/死循环/文件冲突）。
    /// </summary>
    /// <param name="activeNodes">当前活跃的节点列表</param>
    /// <param name="nodeModifiedFiles">可选的节点修改文件映射（节点ID → 修改文件列表），用于运行时文件冲突检测</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<NodeHealthReport> CheckHealthAsync(
        IReadOnlyList<GoalNodePayload> activeNodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? nodeModifiedFiles = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 观察循环状态并决定是否终止（负评趋势/僵局/硬上限）。
    /// </summary>
    /// <param name="context">循环观察上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>true 表示建议终止循环</returns>
    Task<bool> ObserveLoopAsync(LoopObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 评估节点执行结果的质量评分（多维度：完整性/正确性/格式/无幻觉）。
    /// </summary>
    /// <param name="nodeOutput">节点执行输出</param>
    /// <param name="criteria">评分标准（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>质量评分结果（0.0-1.0）</returns>
    Task<NodeQualityScore> ScoreAsync(string nodeOutput, IReadOnlyList<string>? criteria = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 循环观察上下文 — 传递给检查器的循环状态快照
/// </summary>
public sealed record LoopObservationContext {
    /// <summary>获取目标标识。</summary>
    public required string GoalId { get; init; }
    /// <summary>获取节点标识。</summary>
    public required string NodeId { get; init; }
    /// <summary>获取循环迭代次数。</summary>
    public required int LoopIteration { get; init; }
    /// <summary>获取负面评审次数。</summary>
    public required int NegativeReviewCount { get; init; }
    /// <summary>获取总消耗 Token 数。</summary>
    public required int TotalTokensConsumed { get; init; }
    /// <summary>获取已完成回合数。</summary>
    public required int TotalTurnsCompleted { get; init; }
    /// <summary>获取最后节点输出。</summary>
    public string? LastNodeOutput { get; init; }
    /// <summary>获取负面评审任务标识。</summary>
    public string? NegativeReviewTaskId { get; init; }
}

/// <summary>
/// 节点质量评分结果
/// </summary>
public sealed class NodeQualityScore {
    /// <summary>获取总体评分。</summary>
    public double Overall { get; init; }
    /// <summary>获取各维度评分字典。</summary>
    public IReadOnlyDictionary<string, double> Dimensions { get; init; } = new Dictionary<string, double>();
    /// <summary>获取评分原因。</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>获取默认评分。</summary>
    public static NodeQualityScore Default => new() { Overall = 0.5, Reason = "默认评分" };
}