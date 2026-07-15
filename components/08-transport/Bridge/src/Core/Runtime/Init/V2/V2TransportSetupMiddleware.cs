
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
        var sdkUrl = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl(ctx.Credentials!.ApiBaseUrl, ctx.SessionId!);
        IReplBridgeTransport transport;
        try
        {
            transport = ctx.TransportFactory.CreateV2Transport(sdkUrl, ctx.SessionId!, ctx.Credentials.WorkerJwt, ctx.Config.ConnectTimeoutMs);
        }
        catch (Exception ex)
        {
            ctx.Logger?.LogError("Bridge: v2 transport setup failed: {Message}", ex.Message);
            ctx.Fail($"Transport setup failed: {ex.Message}");
            _ = BridgeSessionApi.ArchiveAsync(
                ctx.SessionId!, ctx.Parameters.BaseUrl, ctx.AccessToken!,
                ctx.Parameters.OrgUUID, ctx.Config.HttpTimeoutMs, ctx.HttpClient, ct);
            return;
        }

        ctx.Transport = transport;
        ctx.Parameters.OnStateChange?.Invoke(BridgeState.Ready, null);
        await next(ctx, ct).ConfigureAwait(false);
    }
}
