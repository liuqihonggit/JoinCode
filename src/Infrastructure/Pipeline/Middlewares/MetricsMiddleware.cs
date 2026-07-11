namespace Infrastructure.Pipeline.Middlewares;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 通用指标记录中间件 — 管道执行后自动记录 count 和可选 duration 指标
/// 适用于所有实现 IMetricsContext 的管道上下文
/// </summary>
public sealed class MetricsMiddleware<TContext> : IMiddleware<TContext>
    where TContext : IMetricsContext
{
    private readonly ITelemetryService? _telemetryService;

    public MetricsMiddleware(ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
    }

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (_telemetryService == null) return;

        var tags = context.BuildMetricsTags();
        tags["success"] = context.IsMetricsSuccess.ToString();

        _telemetryService.RecordCount($"{context.MetricsPrefix}.count", tags, description: $"{context.MetricsPrefix} count");

        if (context.MetricsDurationMs.HasValue)
        {
            _telemetryService.RecordHistogram($"{context.MetricsPrefix}.duration", context.MetricsDurationMs.Value, tags, "ms", $"{context.MetricsPrefix} duration");
        }
    }
}
