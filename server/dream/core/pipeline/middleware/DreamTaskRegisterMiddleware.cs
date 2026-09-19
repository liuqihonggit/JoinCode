namespace JoinCode.Dream.Pipeline;


/// <summary>
/// Dream 任务注册中间件 — 向任务注册表登记本次 Dream 会话,设置任务标识与时间窗口
/// </summary>
[Register(typeof(IDreamMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DreamTaskRegisterMiddleware : ServiceEntity, IDreamMiddleware {
    private readonly IDreamTaskRegistry _taskRegistry;
    private readonly AutoDreamConfig _config;

    /// <summary>
    /// 构造 Dream 任务注册中间件
    /// </summary>
    /// <param name="taskRegistry">Dream 任务注册表</param>
    /// <param name="config">自动 Dream 配置</param>
    public DreamTaskRegisterMiddleware(IDreamTaskRegistry taskRegistry, AutoDreamConfig config) {
        _taskRegistry = taskRegistry;
        _config = config;
    }

    /// <summary>
    /// 执行中间件 — 注册 Dream 任务并填充上下文任务标识,然后调用后续中间件
    /// </summary>
    /// <param name="ctx">Dream 管道上下文</param>
    /// <param name="next">后续中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步任务</returns>
    public async Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct) {
        var taskId = await _taskRegistry.RegisterDreamTaskAsync(
            new DreamTaskRegistrationRequest(
                ctx.SessionIds.Count(),
                DateTime.UtcNow.AddHours(-_config.MinHours).Ticks / TimeSpan.TicksPerMillisecond,
                new CancellationTokenSource()),
            ct).ConfigureAwait(false);

        ctx.TaskId = taskId;
        ctx.TaskRegistered = true;

        await next(ctx, ct).ConfigureAwait(false);
    }
}