namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Goal 层用户交互服务 — 带超时的权限询问
/// 用途: 负向评价循环中，负评6~10条时询问用户是否继续
/// 超时后协调者自动接管（用户可能睡觉/离开）
/// 不修改底层 IInteractiveService，Goal 层专用
/// </summary>
public interface IGoalUserInteraction
{
    /// <summary>
    /// 带超时询问用户是否继续循环
    /// </summary>
    /// <param name="question">提问内容</param>
    /// <param name="negativeReviewCount">当前负评条数</param>
    /// <param name="loopIteration">当前循环迭代次数</param>
    /// <param name="timeoutSeconds">超时秒数（默认60秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户决策结果</returns>
    Task<GoalUserDecision> AskToContinueAsync(
        string question,
        int negativeReviewCount,
        int loopIteration,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Goal 用户决策结果
/// </summary>
public sealed record GoalUserDecision
{
    /// <summary>用户选择继续循环</summary>
    public bool ShouldContinue { get; init; }

    /// <summary>协调者接管（超时或用户明确离开）</summary>
    public bool CoordinatorTakenOver { get; init; }

    /// <summary>决策原因</summary>
    public string? Reason { get; init; }

    public static GoalUserDecision Continue(string? reason = null) => new()
    {
        ShouldContinue = true,
        Reason = reason ?? "User chose to continue",
    };

    public static GoalUserDecision Stop(string? reason = null) => new()
    {
        ShouldContinue = false,
        Reason = reason ?? "User chose to stop",
    };

    public static GoalUserDecision CoordinatorTakeover(string? reason = null) => new()
    {
        ShouldContinue = false,
        CoordinatorTakenOver = true,
        Reason = reason ?? "Coordinator takeover due to timeout",
    };
}
