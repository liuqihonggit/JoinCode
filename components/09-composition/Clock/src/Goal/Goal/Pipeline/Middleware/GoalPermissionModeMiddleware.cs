namespace Core.Goal;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 权限模式中间件 — Start 时切换 Auto，Clear/MarkCompleted/MarkUnmet 时恢复
/// </summary>
[Register(typeof(IGoalLifecycleMiddleware))]
public sealed partial class GoalPermissionModeMiddleware : IGoalLifecycleMiddleware
{
    [Inject] private readonly ILogger<GoalPermissionModeMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(GoalLifecycleContext ctx, MiddlewareDelegate<GoalLifecycleContext> next, CancellationToken ct)
    {
        if (ctx.PermissionManager is null)
        {
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        switch (ctx.Operation)
        {
            case GoalOperation.Start:
                try
                {
                    ctx.SavedPermissionMode = await ctx.PermissionManager.GetCurrentModeAsync(ct).ConfigureAwait(false);
                    await ctx.PermissionManager.SetPermissionModeAsync(PermissionMode.Auto, ct).ConfigureAwait(false);
                    _logger?.LogInformation(L.T(StringKey.PermissionModeSwitched), ctx.SavedPermissionMode);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, L.T(StringKey.PermissionModeSwitchFailed));
                    ctx.SavedPermissionMode = null;
                }
                break;

            case GoalOperation.Clear:
            case GoalOperation.MarkCompleted:
            case GoalOperation.MarkUnmet:
                if (ctx.SavedPermissionMode.HasValue)
                {
                    try
                    {
                        await ctx.PermissionManager.SetPermissionModeAsync(ctx.SavedPermissionMode.Value, ct).ConfigureAwait(false);
                        _logger?.LogInformation(L.T(StringKey.PermissionModeRestored), ctx.SavedPermissionMode.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, L.T(StringKey.PermissionModeRestoreFailed));
                    }
                }
                break;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
