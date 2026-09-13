namespace Core.Scheduling.Tasks;


/// <summary>
/// 计划模式中间件 — 当 Teammate 定义要求计划模式且当前未处于计划模式时，自动进入计划模式
/// </summary>
[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammatePlanModeMiddleware : ServiceEntity, ITeammateExecutionMiddleware
{

    /// <summary>
    /// 初始化计划模式中间件
    /// </summary>
    /// <param name="planModeManager">计划模式管理器，为 null 时跳过计划模式处理</param>
    /// <param name="logger">日志记录器</param>
    public TeammatePlanModeMiddleware(IPlanModeManager? planModeManager = null, ILogger<TeammatePlanModeMiddleware>? logger = null)
    {
        _planModeManager = planModeManager;
        _logger = logger;
    }
    private readonly IPlanModeManager? _planModeManager;
    private readonly ILogger<TeammatePlanModeMiddleware>? _logger;

    /// <inheritdoc/>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        if (ctx.Definition.PlanModeRequired && _planModeManager != null && !_planModeManager.IsInPlanMode)
        {
            try
            {
                _logger?.LogInformation("Teammate {TeammateId} requires plan mode, entering automatically", ctx.Definition.TeammateId);

                var planResult = await _planModeManager.EnterPlanModeAsync(
                    description: $"Teammate {ctx.Definition.TeammateId}: {ctx.Definition.Task}",
                    cancellationToken: ct).ConfigureAwait(false);

                if (!planResult.Success)
                {
                    _logger?.LogWarning("Teammate {TeammateId} failed to enter plan mode: {Error}", ctx.Definition.TeammateId, planResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Teammate {TeammateId} failed to enter plan mode", ctx.Definition.TeammateId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
