
namespace Core.Bridge.Init.V2;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V2 获取 bridge 凭证 — 对齐 TS 端: fetchRemoteCredentials
/// </summary>
[Register]
internal sealed partial class V2CredentialsMiddleware : IMiddleware<V2BridgeInitContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(V2BridgeInitContext ctx, MiddlewareDelegate<V2BridgeInitContext> next, CancellationToken ct)
    {
        var credentials = await BridgeRemoteCore.WithRetryAsync(
            () => BridgeRemoteCore.FetchCredentialsWithDeviceTokenAsync(
                ctx.SessionId!, ctx.Parameters, ctx.Config.HttpTimeoutMs, ctx.HttpClient, ctx.AccessToken!, ct),
            "fetchRemoteCredentials",
            ctx.Config.InitRetryMaxAttempts,
            ctx.Config.InitRetryBaseDelayMs,
            ctx.Config.InitRetryMaxDelayMs,
            ctx.Config.InitRetryJitterFraction,
            ct).ConfigureAwait(false);

        if (credentials is null)
        {
            ctx.Fail("Remote credentials fetch failed — see debug log");
            // 对齐 TS 端: 凭证获取失败时归档会话
            _ = BridgeSessionApi.ArchiveAsync(
                ctx.SessionId!, ctx.Parameters.BaseUrl, ctx.AccessToken!,
                ctx.Parameters.OrgUUID, ctx.Config.HttpTimeoutMs, ctx.HttpClient, ct);
            return;
        }

        ctx.Credentials = credentials;
        await next(ctx, ct).ConfigureAwait(false);
    }
}
