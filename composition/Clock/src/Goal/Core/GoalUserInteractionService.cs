namespace Core.Goal;

using JoinCode.Abstractions.Interfaces;

/// <summary>
/// Goal 层用户交互服务 — 带超时的权限询问
/// 封装 IInteractiveService，1分钟超时后协调者自动接管
/// </summary>
[Register(typeof(IGoalUserInteraction))]
public sealed class GoalUserInteractionService : ServiceEntity, IGoalUserInteraction
{
    private readonly IInteractiveService _interactiveService;
    private readonly ILogger<GoalUserInteractionService>? _logger;

    public GoalUserInteractionService(IInteractiveService interactiveService, ILogger<GoalUserInteractionService>? logger = null)
    {
        _interactiveService = interactiveService;
        _logger = logger;
    }

    public async Task<GoalUserDecision> AskToContinueAsync(
        string question,
        int negativeReviewCount,
        int loopIteration,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var options = new List<string> { "继续循环", "停止循环" };

            var result = await _interactiveService.AskUserQuestionAsync(
                $"[Goal 权限] {question} (负评:{negativeReviewCount}, 迭代:{loopIteration})",
                options,
                cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            if (!result.Success || result.Cancelled)
            {
                _logger?.LogWarning("[GoalUserInteraction] 用户拒绝回答或交互失败，协调者接管");
                return GoalUserDecision.CoordinatorTakeover("User declined or interaction failed");
            }

            var answer = result.Answer ?? string.Empty;
            var shouldContinue = answer.Contains("继续", StringComparison.OrdinalIgnoreCase);

            _logger?.LogInformation("[GoalUserInteraction] 用户决策: {Decision} (负评:{NegCount}, 迭代:{Iter})",
                shouldContinue ? "继续" : "停止", negativeReviewCount, loopIteration);

            return shouldContinue
                ? GoalUserDecision.Continue()
                : GoalUserDecision.Stop();
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning("[GoalUserInteraction] 用户交互超时({Timeout}s)，协调者接管 (负评:{NegCount}, 迭代:{Iter})",
                timeoutSeconds, negativeReviewCount, loopIteration);

            return GoalUserDecision.CoordinatorTakeover($"Timeout after {timeoutSeconds}s");
        }
    }
}
