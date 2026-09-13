namespace Core.Bridge.Init.V2;


/// <summary>
/// V2 创建 code session — 对齐 TS 端: createCodeSession
/// </summary>
[Register(typeof(IMiddleware<V2BridgeInitContext>), ServiceLifetime.Singleton)]
internal sealed partial class V2CodeSessionMiddleware : ServiceEntity, IMiddleware<V2BridgeInitContext>
{

    /// <summary>
    /// 执行中间件 — 创建 code session 并更新会话标题（best-effort），调用下一中间件
    /// </summary>
    /// <param name="ctx">V2 Bridge 初始化上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(V2BridgeInitContext ctx, MiddlewareDelegate<V2BridgeInitContext> next, CancellationToken ct)
    {
        var accessToken = ctx.AccessToken ?? throw new InvalidOperationException("AccessToken not set.");
        var sessionId = await BridgeRemoteCore.WithRetryAsync(
            () => BridgeCodeSessionApi.CreateCodeSessionAsync(
                ctx.Parameters.BaseUrl,
                accessToken,
                ctx.Parameters.Title,
                ctx.Config.HttpTimeoutMs,
                ctx.HttpClient,
                ctx.Parameters.Tags,
                ct),
            "createCodeSession",
            ctx.Config.InitRetryMaxAttempts,
            ctx.Config.InitRetryBaseDelayMs,
            ctx.Config.InitRetryMaxDelayMs,
            ctx.Config.InitRetryJitterFraction,
            ct).ConfigureAwait(false);

        if (sessionId is null)
        {
            ctx.Fail("Session creation failed — see debug log");
            return;
        }

        ctx.SessionId = sessionId;

        // 对齐 TS 端: updateBridgeSessionTitle — best-effort
        _ = BridgeSessionApi.UpdateTitleAsync(
            sessionId, ctx.Parameters.Title,
            ctx.Parameters.BaseUrl,
            ctx.AccessToken ?? throw new InvalidOperationException("AccessToken not set."),
            ctx.Parameters.OrgUUID,
            ctx.HttpClient,
            ct).ConfigureAwait(false);

        await next(ctx, ct).ConfigureAwait(false);
    }
}
