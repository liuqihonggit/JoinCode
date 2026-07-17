
namespace Core.Bridge.Init.V2;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V2 获取 bridge 凭证 — 对齐 TS 端: fetchRemoteCredentials
/// </summary>
[Register]
internal sealed partial class V2CredentialsMiddleware : IMiddleware<V2BridgeInitContext>
{

    public async Task InvokeAsync(V2BridgeInitContext ctx, MiddlewareDelegate<V2BridgeInitContext> next, CancellationToken ct)
    {
        var sessionId = ctx.SessionId ?? throw new InvalidOperationException("SessionId is not set.");
        var accessToken = ctx.AccessToken ?? throw new InvalidOperationException("AccessToken is not set.");

        var credentials = await BridgeRemoteCore.WithRetryAsync(
            () => BridgeRemoteCore.FetchCredentialsWithDeviceTokenAsync(
                sessionId, ctx.Parameters, ctx.Config.HttpTimeoutMs, ctx.HttpClient, accessToken, ct),
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
                sessionId, ctx.Parameters.BaseUrl, accessToken,
                ctx.Parameters.OrgUUID, ctx.Config.HttpTimeoutMs, ctx.HttpClient, ct);
            return;
        }

        ctx.Credentials = credentials;
        await next(ctx, ct).ConfigureAwait(false);
    }
}
