namespace Core.Goal;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 心跳控制中间件 — Pause/Clear/MarkCompleted/MarkUnmet 时重置心跳
/// </summary>
[Register(typeof(IGoalLifecycleMiddleware))]
public sealed partial class GoalHeartbeatControlMiddleware : IGoalLifecycleMiddleware
{

    public Task InvokeAsync(GoalLifecycleContext ctx, MiddlewareDelegate<GoalLifecycleContext> next, CancellationToken ct)
    {
        switch (ctx.Operation)
        {
            case GoalOperation.Pause:
            case GoalOperation.Clear:
            case GoalOperation.MarkCompleted:
            case GoalOperation.MarkUnmet:
                ctx.ShouldResetHeartbeat = true;
                break;
        }

        return next(ctx, ct);
    }
}
