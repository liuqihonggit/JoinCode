namespace Memdir.Sync;


/// <summary>
/// 启动完成中间件 — 标记运行状态、记录日志和指标
/// </summary>
[Register(typeof(ISyncStartMiddleware), ServiceLifetime.Singleton)]
public sealed partial class StartCompletionMiddleware : ServiceEntity, ISyncStartMiddleware
{

    /// <summary>
    /// 创建启动完成中间件实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    /// <param name="telemetryService">可选的遥测服务</param>
    public StartCompletionMiddleware(ILogger<StartCompletionMiddleware>? logger = null, ITelemetryService? telemetryService = null)
    {
        _logger = logger;
        _telemetryService = telemetryService;
    }
    private readonly ILogger<StartCompletionMiddleware>? _logger;
    private readonly ITelemetryService? _telemetryService;


    /// <inheritdoc />
    public Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct)
    {
        if (ctx.Failed)
        {
            return next(ctx, ct);
        }

        ctx.MarkAsRunning = true;
        _logger?.LogInformation(L.T(StringKey.VaultLogSyncStarted), ctx.Options.WatchPath);
        ToolTelemetryHelper.RecordToolCount(_telemetryService, "sync.memory.count", "start", true, "Memory sync count");

        return next(ctx, ct);
    }
}
