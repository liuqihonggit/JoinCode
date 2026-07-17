namespace Infrastructure.Pipeline.Middlewares;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 通用 Token 验证中间件 — 检查 OAuth token 是否存在
/// 适用于所有需要 Token 验证的初始化管道
/// </summary>
public sealed class TokenValidationMiddleware<TContext> : IMiddleware<TContext>
    where TContext : ITokenValidationContext
{

    public Task InvokeAsync(TContext ctx, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        var accessToken = ctx.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            ctx.Fail("No OAuth token");
            return Task.CompletedTask;
        }

        ctx.AccessToken = accessToken;
        return next(ctx, ct);
    }
}
