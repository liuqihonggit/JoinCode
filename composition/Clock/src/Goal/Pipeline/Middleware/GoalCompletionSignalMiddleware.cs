namespace Core.Goal;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 完成信号中间件 — MarkCompleted/MarkUnmet 时设置完成信号
/// </summary>
[Register(typeof(IGoalLifecycleMiddleware))]
public sealed partial class GoalCompletionSignalMiddleware : IGoalLifecycleMiddleware
{

    public Task InvokeAsync(GoalLifecycleContext ctx, MiddlewareDelegate<GoalLifecycleContext> next, CancellationToken ct)
    {
        switch (ctx.Operation)
        {
            case GoalOperation.MarkCompleted:
            case GoalOperation.MarkUnmet:
                ctx.ShouldSignalCompletion = true;
                break;
        }

        return next(ctx, ct);
    }
}
