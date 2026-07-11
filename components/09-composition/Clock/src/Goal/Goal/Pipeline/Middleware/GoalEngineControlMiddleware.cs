namespace Core.Goal;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 引擎控制中间件 — Start/Resume 时标记启动循环，Clear/MarkCompleted/MarkUnmet 时标记取消循环
/// </summary>
[Register(typeof(IGoalLifecycleMiddleware))]
public sealed partial class GoalEngineControlMiddleware : IGoalLifecycleMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(GoalLifecycleContext ctx, MiddlewareDelegate<GoalLifecycleContext> next, CancellationToken ct)
    {
        switch (ctx.Operation)
        {
            case GoalOperation.Start:
            case GoalOperation.Resume:
                ctx.ShouldStartEngineLoop = true;
                break;

            case GoalOperation.Clear:
            case GoalOperation.MarkCompleted:
            case GoalOperation.MarkUnmet:
                ctx.ShouldCancelEngineLoop = true;
                break;
        }

        return next(ctx, ct);
    }
}
