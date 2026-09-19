namespace Core.Scheduling.Tasks;


/// <summary>
/// Teammate 校验中间件 — 在执行前校验 Teammate 定义并记录启动日志
/// </summary>
[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammateValidationMiddleware : ServiceEntity, ITeammateExecutionMiddleware {

    /// <summary>
    /// 初始化 Teammate 校验中间件
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public TeammateValidationMiddleware(ILogger<TeammateValidationMiddleware>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<TeammateValidationMiddleware>? _logger;


    /// <inheritdoc/>
    public Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(ctx.Definition);

        _logger?.LogInformation(L.T(StringKey.InProcessTeammateStartLog),
            ctx.Definition.TeammateId, ctx.Definition.Task, ctx.Definition.ContinuousMode);

        return next(ctx, ct);
    }
}