namespace Core.Goal;


/// <summary>
/// Goal 层用户交互服务 — 带超时的权限询问
/// 封装 IInteractiveService，1分钟超时后协调者自动接管
/// </summary>
[Register(typeof(IGoalUserInteraction), ServiceLifetime.Singleton)]
public sealed class GoalUserInteractionService : ServiceEntity, IGoalUserInteraction {
    private readonly IInteractiveService _interactiveService;
    private readonly IToolPermissionManager? _permissionManager;
    private readonly ILogger<GoalUserInteractionService>? _logger;

    /// <summary>
    /// 构造 GoalUserInteractionService — 注入交互服务、权限管理器与可选日志记录器
    /// </summary>
    /// <param name="interactiveService">底层用户交互服务</param>
    /// <param name="permissionManager">权限管理器（用于在非交互模式跳过 ask）</param>
    /// <param name="logger">可选日志记录器</param>
    public GoalUserInteractionService(
        IInteractiveService interactiveService,
        IToolPermissionManager? permissionManager = null,
        ILogger<GoalUserInteractionService>? logger = null) {
        _interactiveService = interactiveService;
        _permissionManager = permissionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GoalUserDecision> AskToContinueAsync(
        string question,
        int negativeReviewCount,
        int loopIteration,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default) {
        // 非交互模式不得走 IInteractiveService — 后台/无人值守没有宿主可回答问题，
        // 继续调用会把 Goal 循环挂死在 TUI 同步阻塞上（TASK105 D5）。
        if (_permissionManager is not null) {
            var mode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            if (mode is PermissionMode.Unattended or PermissionMode.Bypass) {
                _logger?.LogInformation("[GoalUserInteraction] {Mode} 模式跳过交互询问，停止负评循环 (负评:{NegCount}, 迭代:{Iter})",
                    mode, negativeReviewCount, loopIteration);
                return GoalUserDecision.Stop($"Non-interactive mode: {mode}");
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try {
            var options = new List<string> { "继续循环", "停止循环" };

            var result = await _interactiveService.AskUserQuestionAsync(
                $"[Goal 权限] {question} (负评:{negativeReviewCount}, 迭代:{loopIteration})",
                options,
                cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            if (!result.Success || result.Cancelled) {
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
        } catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            _logger?.LogWarning("[GoalUserInteraction] 用户交互超时({Timeout}s)，协调者接管 (负评:{NegCount}, 迭代:{Iter})",
                timeoutSeconds, negativeReviewCount, loopIteration);

            return GoalUserDecision.CoordinatorTakeover($"Timeout after {timeoutSeconds}s");
        }
    }
}