
namespace Core.Goal;

/// <summary>
/// 目标评估器接口 — 评估目标是否已完成
/// </summary>
public interface IGoalEvaluator {
    /// <summary>
    /// 异步评估目标是否达成
    /// </summary>
    /// <param name="objective">目标描述</param>
    /// <param name="constraints">约束条件列表</param>
    /// <param name="recentConversation">近期对话上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>目标评估结果</returns>
    Task<GoalEvaluationResult> EvaluateAsync(
        string objective,
        IReadOnlyList<string> constraints,
        string recentConversation,
        CancellationToken cancellationToken = default);
}