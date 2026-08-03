namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Goal 循环窥探接口 — 协调者可观察循环状态并决定是否终止
/// 用途: 负向评价循环中，协调者在不接管的情况下监控循环进度
/// </summary>
public interface IGoalLoopObserver
{
    /// <summary>
    /// 观察循环状态并决定是否终止
    /// </summary>
    /// <param name="context">循环观察上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>true 表示协调者要求终止循环</returns>
    Task<bool> ObserveAsync(LoopObservationContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 循环观察上下文 — 传递给协调者的循环状态快照
/// </summary>
public sealed record LoopObservationContext
{
    public required string GoalId { get; init; }
    public required string NodeId { get; init; }
    public required int LoopIteration { get; init; }
    public required int NegativeReviewCount { get; init; }
    public required int TotalTokensConsumed { get; init; }
    public required int TotalTurnsCompleted { get; init; }
    public string? LastNodeOutput { get; init; }
    public string? NegativeReviewTaskId { get; init; }
}
