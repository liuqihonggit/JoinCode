
namespace Core.Bridge.Init.V2;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V2 建立传输 — 对齐 TS 端: createV2Transport
/// </summary>
[Register]
internal sealed partial class V2TransportSetupMiddleware : IMiddleware<V2BridgeInitContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(V2BridgeInitContext ctx, MiddlewareDelegate<V2BridgeInitContext> next, CancellationToken ct)
    {
        var credentials = ctx.Credentials ?? throw new InvalidOperationException("Credentials is not set. Ensure V2CredentialsMiddleware runs first.");
        var sessionId = ctx.SessionId ?? throw new InvalidOperationException("SessionId is not set. Ensure TokenValidationMiddleware runs first.");
        var accessToken = ctx.AccessToken ?? throw new InvalidOperationException("AccessToken is not set.");

        var sdkUrl = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl(credentials.ApiBaseUrl, sessionId);
        IReplBridgeTransport transport;
        try
        {
            transport = ctx.TransportFactory.CreateV2Transport(sdkUrl, sessionId, credentials.WorkerJwt, ctx.Config.ConnectTimeoutMs);
        }
        catch (Exception ex)
        {
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
