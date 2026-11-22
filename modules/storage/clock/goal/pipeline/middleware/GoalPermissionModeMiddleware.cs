namespace Core.Goal;


/// <summary>
/// 权限模式中间件 — Start 时切换 Unattended（后台 Goal 不弹 ask），Clear/MarkCompleted/MarkUnmet 时恢复
/// </summary>
[Register(typeof(IGoalLifecycleMiddleware), ServiceLifetime.Singleton)]
public sealed partial class GoalPermissionModeMiddleware : ServiceEntity, IGoalLifecycleMiddleware {

    /// <summary>
    /// 构造 GoalPermissionModeMiddleware — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public GoalPermissionModeMiddleware(ILogger<GoalPermissionModeMiddleware>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<GoalPermissionModeMiddleware>? _logger;

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(GoalLifecycleContext ctx, MiddlewareDelegate<GoalLifecycleContext> next, CancellationToken ct) {
        if (ctx.PermissionManager is null) {
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        switch (ctx.Operation) {
            case GoalOperation.Start:
            try {
                var currentMode = await ctx.PermissionManager.GetCurrentModeAsync(ct).ConfigureAwait(false);
                if (currentMode is PermissionMode.Bypass or PermissionMode.Unattended) {
                    // Bypass 不能被降级为 Unattended（会丢掉全放行语义）；已经是 Unattended 则无需切换
                    ctx.SavedPermissionMode = null;
                    _logger?.LogInformation("[GoalPermissionMode] 当前权限模式为 {Mode}，跳过切换到 Unattended", currentMode);
                    break;
                }

                ctx.SavedPermissionMode = currentMode;
                await ctx.PermissionManager.SetPermissionModeAsync(PermissionMode.Unattended, ct).ConfigureAwait(false);
                _logger?.LogInformation(L.T(StringKey.PermissionModeSwitched), ctx.SavedPermissionMode, PermissionMode.Unattended);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, L.T(StringKey.PermissionModeSwitchFailed));
                ctx.SavedPermissionMode = null;
            }
            break;

            case GoalOperation.Clear:
            case GoalOperation.MarkCompleted:
            case GoalOperation.MarkUnmet:
            if (ctx.SavedPermissionMode.HasValue) {
                try {
                    await ctx.PermissionManager.SetPermissionModeAsync(ctx.SavedPermissionMode.Value, ct).ConfigureAwait(false);
                    _logger?.LogInformation(L.T(StringKey.PermissionModeRestored), ctx.SavedPermissionMode.Value);
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, L.T(StringKey.PermissionModeRestoreFailed));
                }
            }
            break;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}