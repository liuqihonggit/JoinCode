namespace Core.Goal;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 状态校验中间件 — 根据操作类型校验当前状态是否允许该操作
/// </summary>
[Register(typeof(IGoalLifecycleMiddleware))]
public sealed partial class GoalStateValidationMiddleware : IGoalLifecycleMiddleware
{

    public Task InvokeAsync(GoalLifecycleContext ctx, MiddlewareDelegate<GoalLifecycleContext> next, CancellationToken ct)
    {
        var status = ctx.State.Status;

        var valid = ctx.Operation switch
        {
            GoalOperation.Start => status != GoalStatus.Pursuing,
            GoalOperation.Pause => status == GoalStatus.Pursuing,
            GoalOperation.Resume => status == GoalStatus.Paused,
            GoalOperation.Clear => status != GoalStatus.Unmet || ctx.State.GoalId is not null,
            GoalOperation.MarkCompleted => status == GoalStatus.Pursuing,
            GoalOperation.MarkUnmet => status == GoalStatus.Pursuing,
            _ => true
        };

        if (!valid)
        {
            ctx.Fail($"Invalid operation {ctx.Operation} for current status {status}");
            return Task.CompletedTask;
        }

        return next(ctx, ct);
    }
}
