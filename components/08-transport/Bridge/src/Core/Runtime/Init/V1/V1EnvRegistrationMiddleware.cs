
namespace Core.Bridge.Init.V1;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V1 注册 Bridge 环境 — 对齐 TS 端: registerBridgeEnvironment
/// </summary>
[Register]
internal sealed partial class V1EnvRegistrationMiddleware : IMiddleware<V1BridgeInitContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct)
    {
        var apiClient = new BridgeApiClient(ctx.HttpClient, new BridgeApiOptions
        {
            BaseUrl = ctx.Parameters.BaseUrl,
            ApiKey = ctx.AccessToken ?? throw new InvalidOperationException("AccessToken not set"),
            GetAccessToken = ctx.Parameters.GetAccessToken,
            OnAuth401 = ctx.Parameters.OnAuth401,
            GetTrustedDeviceToken = ctx.Parameters.GetTrustedDeviceToken is not null
                ? () => ctx.Parameters.GetTrustedDeviceToken().GetAwaiter().GetResult()
                : null,
        });

        var bridgeConfig = new BridgeEnvironmentRegistration
        {
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

        if (regResponse is null)
        {
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
