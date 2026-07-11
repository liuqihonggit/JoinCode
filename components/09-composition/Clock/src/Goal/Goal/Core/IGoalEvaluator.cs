
namespace Core.Goal;

public interface IGoalEvaluator
{
    Task<GoalEvaluationResult> EvaluateAsync(
        string objective,
        IReadOnlyList<string> constraints,
        string recentConversation,
        CancellationToken cancellationToken = default);
}
