namespace Core.Goal;


/// <summary>
/// 状态变更中间件 — 根据操作类型执行状态转换
/// </summary>
[Register(typeof(IGoalLifecycleMiddleware), ServiceLifetime.Singleton)]
public sealed partial class GoalStateTransitionMiddleware : ServiceEntity, IGoalLifecycleMiddleware {

    /// <summary>
    /// 构造 GoalStateTransitionMiddleware — 注入时钟服务用于记录状态变更时间
    /// </summary>
    /// <param name="clock">时钟服务</param>
    public GoalStateTransitionMiddleware(IClockService clock) {
        _clock = clock;
    }
    private readonly IClockService _clock;


    /// <inheritdoc />
    public Task InvokeAsync(GoalLifecycleContext ctx, MiddlewareDelegate<GoalLifecycleContext> next, CancellationToken ct) {
        switch (ctx.Operation) {
            case GoalOperation.Start:
            ctx.State.Status = GoalStatus.Pursuing;
            break;

            case GoalOperation.Pause:
            ctx.State.Status = GoalStatus.Paused;
            ctx.State.PausedAt = _clock.GetUtcNow();
            break;

            case GoalOperation.Resume:
            ctx.State.Status = GoalStatus.Pursuing;
            ctx.State.PausedAt = null;
            break;

            case GoalOperation.Clear:
            ctx.State.Status = GoalStatus.Unmet;
            ctx.State.AchievedAt = _clock.GetUtcNow();
            break;

            case GoalOperation.MarkCompleted:
            ctx.State.Status = GoalStatus.Achieved;
            ctx.State.AchievedAt = _clock.GetUtcNow();
            ctx.State.LastEvaluation = GoalEvaluationResult.Completed(ctx.Reason ?? "Completed");
            break;

            case GoalOperation.MarkUnmet:
            ctx.State.Status = GoalStatus.Unmet;
            ctx.State.AchievedAt = _clock.GetUtcNow();
            ctx.State.LastEvaluation = GoalEvaluationResult.NotCompleted(ctx.Reason ?? "Not completed");
            break;
        }

        ctx.StateTransitioned = true;
        return next(ctx, ct);
    }
}