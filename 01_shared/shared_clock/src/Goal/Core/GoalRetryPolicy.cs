namespace Core.Goal;

/// <summary>
/// 分级重试决策
/// </summary>
public enum RetryDecision
{
    /// <summary>接受当前结果，不重试</summary>
    Accept,
    /// <summary>重试（只传错误部分，复用 GetAffectedSubgraph）</summary>
    RetryWithPatch,
    /// <summary>放弃，标记为失败</summary>
    Abandon,
}

/// <summary>
/// Goal 分级重试策略 — 基于质量分数决定重试行为。
/// 分数&lt;0.3 立即放弃 | 0.3-0.7 有限重试(≤2次) | &gt;=0.7 接受。
/// 不修改现有 RetryPolicy（Agent/API 层），仅用于 Goal 图执行。
/// </summary>
public sealed class GoalRetryPolicy
{
    private const double AbandonThreshold = 0.3;
    private const double AcceptThreshold = 0.7;
    private const int MaxRetryWithPatch = 2;

    /// <summary>
    /// 根据质量分数和当前重试次数决定重试行为。
    /// </summary>
    /// <param name="score">质量分数（0.0-1.0）</param>
    /// <param name="currentRetryCount">当前已重试次数</param>
    public static RetryDecision Decide(double score, int currentRetryCount)
    {
        if (score >= AcceptThreshold)
            return RetryDecision.Accept;

        if (score < AbandonThreshold)
            return RetryDecision.Abandon;

        if (currentRetryCount >= MaxRetryWithPatch)
            return RetryDecision.Abandon;

        return RetryDecision.RetryWithPatch;
    }
}
