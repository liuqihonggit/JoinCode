
namespace Core.Bridge.Init.V2;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V2 创建 code session — 对齐 TS 端: createCodeSession
/// </summary>
[Register]
internal sealed partial class V2CodeSessionMiddleware : IMiddleware<V2BridgeInitContext>
{

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
