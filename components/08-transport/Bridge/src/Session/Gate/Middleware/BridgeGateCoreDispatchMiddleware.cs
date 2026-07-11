namespace Core.Bridge.Gate;

public sealed class BridgeGateCoreDispatchMiddleware : IBridgeInitGateMiddleware
{
    public async Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        var title = BridgeInit.DeriveSessionTitle(ctx.Options);
        ctx.Title = title;

        var baseUrl = ctx.GetBaseUrl();
        ctx.BaseUrl = baseUrl;

        if (ctx.HttpClient is null || ctx.TransportFactory is null)
        {
            ctx.Logger?.LogError("Bridge: httpClient or transportFactory not provided");
            ctx.Fail("httpClient or transportFactory not provided");
            return;
        }

        var useCcrV2 = BridgeRuntimeGate.IsCcrV2Enabled();

        if (useCcrV2)
        {
            var envLessParams = new BridgeEnvLessParams
            {
                BaseUrl = baseUrl,
                OrgUUID = ctx.OrgUUID!,
                Title = title,
                GetAccessToken = ctx.GetAccessToken,
                OnInboundMessage = ctx.Options.OnInboundMessage,
                OnUserMessage = BridgeInit.CreateOnUserMessage(ctx.Options, baseUrl, ctx.GetAccessToken),
                OnPermissionResponse = ctx.Options.OnPermissionResponse,
                OnInterrupt = ctx.Options.OnInterrupt,
                OnSetModel = ctx.Options.OnSetModel,
                OnSetMaxThinkingTokens = ctx.Options.OnSetMaxThinkingTokens,
                OnSetPermissionMode = ctx.Options.OnSetPermissionMode,
                OnStateChange = ctx.Options.OnStateChange,
                OutboundOnly = ctx.Options.OutboundOnly,
                Tags = ctx.Options.Tags,
                InitialMessages = ctx.Options.InitialMessages,
                InitialHistoryCap = 200,
                GetTrustedDeviceToken = ctx.Options.GetTrustedDeviceToken,
            };

            ctx.Handle = await BridgeRemoteCore.InitEnvLessBridgeCoreAsync(
                envLessParams, ctx.HttpClient, ctx.TransportFactory, ctx.V2Pipeline!, ctx.Logger, ct).ConfigureAwait(false);
        }
        else
        {
            var coreParams = new BridgeCoreParams
            {
                Dir = Environment.CurrentDirectory,
                MachineName = Environment.MachineName,
                Branch = "main",
                Title = title,
                BaseUrl = baseUrl,
                SessionIngressUrl = BridgeInit.ResolveSessionIngressUrl(baseUrl),
                WorkerType = "tengu",
                GetAccessToken = ctx.GetAccessToken,
                CreateSession = (envId, sessionTitle, gitRepoUrl, token, cts) =>
                    BridgeInit.CreateSessionViaApiAsync(baseUrl, token, envId, sessionTitle, ctx.HttpClient!, cts),
                ArchiveSession = (sid, cts) =>
                    BridgeSessionApi.ArchiveAsync(sid, baseUrl, ctx.GetAccessToken()!,
                        ctx.OrgUUID!, 30000, ctx.HttpClient!, cts),
                OnInboundMessage = ctx.Options.OnInboundMessage,
                OnUserMessage = BridgeInit.CreateOnUserMessage(ctx.Options, baseUrl, ctx.GetAccessToken),
                OnPermissionResponse = ctx.Options.OnPermissionResponse,
                OnInterrupt = ctx.Options.OnInterrupt,
                OnSetModel = ctx.Options.OnSetModel,
                OnSetMaxThinkingTokens = ctx.Options.OnSetMaxThinkingTokens,
                OnSetPermissionMode = ctx.Options.OnSetPermissionMode,
                OnStateChange = ctx.Options.OnStateChange,
                OutboundOnly = ctx.Options.OutboundOnly,
                Tags = ctx.Options.Tags,
                InitialMessages = ctx.Options.InitialMessages,
                InitialHistoryCap = 200,
                Perpetual = ctx.Options.Perpetual,
                GetTrustedDeviceToken = ctx.Options.GetTrustedDeviceToken,
            };

            ctx.Handle = await BridgeRemoteCore.InitBridgeCoreAsync(
                coreParams, ctx.HttpClient, ctx.FileSystem, ctx.TransportFactory!, ctx.V1Pipeline!, ctx.Logger, ct).ConfigureAwait(false);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
