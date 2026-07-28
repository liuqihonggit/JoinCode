namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 启动完成中间件 — 标记运行状态、记录日志和指标
/// </summary>
[Register(typeof(ISyncStartMiddleware))]
public sealed partial class StartCompletionMiddleware : ISyncStartMiddleware
{
    [Inject] private readonly ILogger<StartCompletionMiddleware>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;


    public Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct)
    {
        if (ctx.Failed)
        {
            return next(ctx, ct);
        }

        ctx.MarkAsRunning = true;
        _logger?.LogInformation(L.T(StringKey.VaultLogSyncStarted), ctx.Options.WatchPath);
        _telemetryService?.RecordCount("sync.memory.count", new Dictionary<string, string> { ["operation"] = "start", ["success"] = "True" }, "count", "Memory sync count");

        return next(ctx, ct);
    }
}
