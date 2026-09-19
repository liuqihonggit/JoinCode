namespace Core.Bridge.Init.V2;


/// <summary>
/// V2 建立传输 — 对齐 TS 端: createV2Transport
/// </summary>
[Register(typeof(IMiddleware<V2BridgeInitContext>), ServiceLifetime.Singleton)]
internal sealed partial class V2TransportSetupMiddleware : ServiceEntity, IMiddleware<V2BridgeInitContext> {

    /// <summary>
    /// 执行中间件 — 校验凭据/会话/令牌后创建 v2 传输,失败时归档会话并终止
    /// </summary>
    /// <param name="ctx">v2 桥接初始化上下文</param>
    /// <param name="next">后续中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步任务</returns>
    public async Task InvokeAsync(V2BridgeInitContext ctx, MiddlewareDelegate<V2BridgeInitContext> next, CancellationToken ct) {
        var credentials = ctx.Credentials ?? throw new InvalidOperationException("Credentials is not set. Ensure V2CredentialsMiddleware runs first.");
        var sessionId = ctx.SessionId ?? throw new InvalidOperationException("SessionId is not set. Ensure TokenValidationMiddleware runs first.");
        var accessToken = ctx.AccessToken ?? throw new InvalidOperationException("AccessToken is not set.");

        var sdkUrl = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl(credentials.ApiBaseUrl, sessionId);
        IReplBridgeTransport transport;
        try {
            transport = ctx.TransportFactory.CreateV2Transport(sdkUrl, sessionId, credentials.WorkerJwt, ctx.Config.ConnectTimeoutMs);
        } catch (Exception ex) {
            ctx.Logger?.LogError("Bridge: v2 transport setup failed: {Message}", ex.Message);
            ctx.Fail($"Transport setup failed: {ex.Message}");
            _ = BridgeSessionApi.ArchiveAsync(
                sessionId, ctx.Parameters.BaseUrl, accessToken,
                ctx.Parameters.OrgUUID, ctx.Config.HttpTimeoutMs, ctx.HttpClient, ct);
            return;
        }

        ctx.Transport = transport;
        ctx.Parameters.OnStateChange?.Invoke(BridgeState.Ready, null);
        await next(ctx, ct).ConfigureAwait(false);
    }
}