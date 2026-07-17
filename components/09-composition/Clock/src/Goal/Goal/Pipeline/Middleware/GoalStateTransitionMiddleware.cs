namespace Core.Goal;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 状态变更中间件 — 根据操作类型执行状态转换
/// </summary>
[Register(typeof(IGoalLifecycleMiddleware))]
public sealed partial class GoalStateTransitionMiddleware : IGoalLifecycleMiddleware
{
    [Inject] private readonly IClockService _clock;


    public Task InvokeAsync(GoalLifecycleContext ctx, MiddlewareDelegate<GoalLifecycleContext> next, CancellationToken ct)
    {
        switch (ctx.Operation)
        {
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
