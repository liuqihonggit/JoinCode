namespace Core.Scheduling.Tasks;


/// <summary>
/// Teammate 执行中间件 — 调用智能体生命周期管理器执行 Teammate 任务，记录遥测指标并处理清理逻辑
/// </summary>
[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammateExecutionMiddleware : ServiceEntity, ITeammateExecutionMiddleware {

    /// <summary>
    /// 初始化 Teammate 执行中间件
    /// </summary>
    /// <param name="agentLifecycleManager">智能体生命周期管理器</param>
    /// <param name="clock">时钟服务</param>
    /// <param name="telemetryService">遥测服务，为 null 时不记录指标</param>
    /// <param name="logger">日志记录器</param>
    public TeammateExecutionMiddleware(IAgentLifecycleManager agentLifecycleManager, IClockService clock, ITelemetryService? telemetryService = null, ILogger<TeammateExecutionMiddleware>? logger = null) {
        _agentLifecycleManager = agentLifecycleManager;
        _clock = clock;
        _telemetryService = telemetryService;
        _logger = logger;
    }
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly ITelemetryService? _telemetryService;
    private readonly ILogger<TeammateExecutionMiddleware>? _logger;
    private readonly IClockService _clock;


    /// <inheritdoc/>
    public async Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct) {
        if (ctx.ContinuousModeHandled) {
            return;
        }

        try {
            var result = await _agentLifecycleManager.ExecuteAsync(ctx.Agent ?? throw new InvalidOperationException("Teammate agent is not available."), ct).ConfigureAwait(false);
            var elapsed = (long)(_clock.GetUtcNow() - ctx.StartTime).TotalMilliseconds;

            if (ctx.CleanupAsync is not null && ctx.State is not null) {
                await ctx.CleanupAsync(ctx.Definition.TeammateId, ctx.State).ConfigureAwait(false);
            }

            RecordTeammateMetrics("execute", result.IsSuccess);

            ctx.Result = result.IsSuccess
                ? AgentTaskResult.Success(ctx.Definition.TaskId, ctx.Definition.TeammateId, result.Output ?? string.Empty, elapsed)
                : AgentTaskResult.Failure(ctx.Definition.TaskId, ctx.Definition.TeammateId, result.Error ?? "Teammate execution failed", elapsed);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            if (ctx.TryCleanupAsync is not null) {
                await ctx.TryCleanupAsync(ctx.Definition.TeammateId).ConfigureAwait(false);
            }
            throw;
        } catch (Exception ex) {
            var elapsed = (long)(_clock.GetUtcNow() - ctx.StartTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.InProcessTeammateFailedLog, ctx.Definition.TeammateId));

            if (ctx.TryCleanupAsync is not null) {
                await ctx.TryCleanupAsync(ctx.Definition.TeammateId).ConfigureAwait(false);
            }

            RecordTeammateMetrics("execute", false);
            ctx.Result = AgentTaskResult.Failure(ctx.Definition.TaskId, ctx.Definition.TeammateId, ex.Message, elapsed);
        }
    }

    private void RecordTeammateMetrics(string operation, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "scheduling.teammate.count", operation, isSuccess, "In-process teammate execution count");
}