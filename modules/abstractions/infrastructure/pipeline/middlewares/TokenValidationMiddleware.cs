namespace Infrastructure.Pipeline.Middlewares;


/// <summary>
/// 通用 Token 验证中间件 — 检查 OAuth token 是否存在
/// 适用于所有需要 Token 验证的初始化管道
/// </summary>
public sealed class TokenValidationMiddleware<TContext> : IMiddleware<TContext>
    where TContext : ITokenValidationContext {

    /// <summary>
    /// 执行中间件 — 校验 OAuth Token 是否存在,缺失则调用上下文 Fail 短路终止
    /// </summary>
    /// <param name="ctx">中间件上下文,需提供 GetAccessToken 与 AccessToken 设置器</param>
    /// <param name="next">下游委托</param>
    /// <param name="ct">取消令牌</param>
    public Task InvokeAsync(TContext ctx, MiddlewareDelegate<TContext> next, CancellationToken ct) {
        var accessToken = ctx.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken)) {
            ctx.Fail("No OAuth token");
            return Task.CompletedTask;
        }

        ctx.AccessToken = accessToken;
        return next(ctx, ct);
    }
}