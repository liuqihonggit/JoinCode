namespace Core.Skills;

/// <summary>
/// 技能遥测中间件 — 启动 Span、记录开始/完成日志、处理异常
/// </summary>
[Register]
public sealed partial class SkillTelemetryMiddleware : ISkillMiddleware
{
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(SkillContext context, MiddlewareDelegate<SkillContext> next, CancellationToken ct)
    {
        var skillName = context.SkillName;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await using var span = _telemetryService?.StartSpan($"skill.{skillName}", TelemetrySpanKind.Server);
        span?.SetTag("skill.name", skillName);

        context.Stopwatch = stopwatch;
        context.Span = span;

        context.ExecutionContext.Logger?.LogInformation(L.T(StringKey.SkillServiceStartExecution), skillName);

        try
        {
            await next(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            context.ExecutionContext.Logger?.LogWarning(L.T(StringKey.SkillServiceExecutionCancelled), skillName);
            span?.SetStatus(TelemetryStatusCode.Error, "Cancelled");
            context.Result = SkillResult.FailureResult(skillName, L.T(StringKey.SkillServiceExecutionCancelledResult));
        }
        catch (Exception ex)
        {
            context.ExecutionContext.Logger?.LogError(ex, L.T(StringKey.SkillServiceExecutionFailed), skillName);
            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            context.Result = SkillResult.FailureResult(skillName, ex.Message);
        }

        stopwatch.Stop();
        span?.SetTag("skill.duration_ms", stopwatch.ElapsedMilliseconds);

        var result = context.Result;
        span?.SetStatus(result?.Success == true ? TelemetryStatusCode.Ok : TelemetryStatusCode.Error);

        context.ExecutionContext.Logger?.LogInformation(
            L.T(StringKey.SkillServiceExecutionComplete),
            skillName, stopwatch.ElapsedMilliseconds);

        if (result != null)
        {
            context.Result = result with { DurationMs = stopwatch.ElapsedMilliseconds };
        }
    }
}
