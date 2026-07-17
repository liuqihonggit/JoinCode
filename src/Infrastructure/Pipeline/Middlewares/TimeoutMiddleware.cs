namespace Infrastructure.Pipeline.Middlewares;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 通用超时中间件（接口约束版）— 从 ITimeoutContext.Timeout 读取超时时长
/// </summary>
public sealed class TimeoutMiddleware<TContext> : IMiddleware<TContext>
    where TContext : ITimeoutContext
{

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, context.Timeout);

        try
        {
            await next(context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            context.IsTimedOut = true;
            throw new TimeoutException($"操作在 {context.Timeout.TotalMilliseconds}ms 内未完成");
        }
    }
}

/// <summary>
/// 通用超时中间件（固定超时版）— 适用于任意 Context，不要求实现 ITimeoutContext
/// </summary>
public sealed class FixedTimeoutMiddleware<TContext>(TimeSpan _timeout) : IMiddleware<TContext>
{

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, _timeout);

        try
        {
            await next(context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"操作在 {_timeout.TotalMilliseconds}ms 内未完成");
        }
    }
}
