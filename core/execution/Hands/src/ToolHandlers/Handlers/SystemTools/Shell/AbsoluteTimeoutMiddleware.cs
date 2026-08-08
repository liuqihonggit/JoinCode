namespace Tools.Shell;

/// <summary>
/// 绝对超时中间件 — 读取 ShellPipelineContext.TimeoutPolicy 强制截断超时上限
/// 替代 FixedTimeoutMiddleware(120s) 硬编码，由类继承体系（OneShotCommandGroup/LongRunningGroup）驱动
/// 位置：管道最前端，在 Validation 之前
/// </summary>
[Register]
public sealed partial class AbsoluteTimeoutMiddleware : ServiceEntity, IShellMiddleware
{
    private readonly ILogger<AbsoluteTimeoutMiddleware>? _logger;

    public AbsoluteTimeoutMiddleware(ILogger<AbsoluteTimeoutMiddleware>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var policy = context.TimeoutPolicy;

        if (policy.AbsoluteTimeoutSeconds is { } absoluteSeconds and > 0)
        {
            var absoluteTimeout = TimeSpan.FromSeconds(absoluteSeconds);
            using var cts = TimeoutHelper.CreateLinkedTimeout(ct, absoluteTimeout);

            try
            {
                await next(context, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger?.LogWarning("Shell 命令绝对超时 ({Seconds}s): {Command}", absoluteSeconds, context.Command);
                throw new TimeoutException($"[ABS001] 命令在 {absoluteSeconds}s 内未完成（绝对超时上限）");
            }
        }
        else
        {
            await next(context, ct).ConfigureAwait(false);
        }
    }
}
