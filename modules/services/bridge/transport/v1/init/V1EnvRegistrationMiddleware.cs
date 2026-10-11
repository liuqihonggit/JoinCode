namespace Core.Bridge.Init.V1;


/// <summary>
/// V1 注册 Bridge 环境 — 对齐 TS 端: registerBridgeEnvironment
/// </summary>
[Register(typeof(IMiddleware<V1BridgeInitContext>), ServiceLifetime.Singleton)]
internal sealed partial class V1EnvRegistrationMiddleware : ServiceEntity, IMiddleware<V1BridgeInitContext> {

    /// <summary>
    /// 执行 V1 环境注册 — 创建 API 客户端、注册 Bridge 环境并将结果写入上下文
    /// </summary>
    /// <param name="ctx">V1 桥初始化上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct) {
        var apiClient = new BridgeApiClient(ctx.HttpClient, new BridgeApiOptions {
            BaseUrl = ctx.Parameters.BaseUrl,
            ApiKey = ctx.AccessToken ?? throw new InvalidOperationException("AccessToken not set"),
            GetAccessToken = ctx.Parameters.GetAccessToken,
            OnAuth401 = ctx.Parameters.OnAuth401,
            GetTrustedDeviceToken = ctx.Parameters.GetTrustedDeviceToken is not null
                ? () => ctx.Parameters.GetTrustedDeviceToken().GetAwaiter().GetResult()
                : null,
        });

        var bridgeConfig = new BridgeEnvironmentRegistration {
            BridgeId = Guid.NewGuid().ToString("N"),
            MachineName = ctx.Parameters.MachineName,
            Dir = ctx.Parameters.Dir,
            Branch = ctx.Parameters.Branch,
            GitRepoUrl = ctx.Parameters.GitRepoUrl,
            WorkerType = ctx.Parameters.WorkerType,
            MaxSessions = 1,
            ReuseEnvironmentId = ctx.PriorPointer?.EnvironmentId,
        };

        var regResponse = await apiClient.RegisterBridgeEnvironmentAsync(bridgeConfig, ct).ConfigureAwait(false);

        if (regResponse is null) {
            ctx.Fail("Environment registration returned null");
            return;
        }

        ctx.ApiClient = apiClient;
        ctx.EnvironmentId = regResponse.EnvironmentId;
        ctx.EnvironmentSecret = regResponse.BridgeId;
        ctx.SessionIngressUrl = regResponse.SessionIngressUrl ?? ctx.Parameters.SessionIngressUrl;

        ctx.Logger?.LogInformation("Bridge v1: 环境已注册: {EnvId}", ctx.EnvironmentId);
        await next(ctx, ct).ConfigureAwait(false);
    }
}