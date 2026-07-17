namespace Infrastructure.Pipeline.Middlewares;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 通用参数非空验证中间件 — 检查必填参数是否为 null
/// 适用于所有需要参数验证的管道
/// </summary>
public sealed class NullCheckValidationMiddleware<TContext> : IMiddleware<TContext>
    where TContext : INullCheckContext
{

    public Task InvokeAsync(TContext ctx, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        foreach (var (name, value) in ctx.RequiredParameters)
        {
            if (value is null)
            {
                ctx.Fail($"Required parameter '{name}' is null");
                return Task.CompletedTask;
            }
        }

        return next(ctx, ct);
    }
}
