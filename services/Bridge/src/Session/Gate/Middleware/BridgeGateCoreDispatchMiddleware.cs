namespace Core.Bridge.Gate;

public sealed class BridgeGateCoreDispatchMiddleware : IBridgeInitGateMiddleware
{
    private readonly INetworkConnectivityService? _networkService;

    public BridgeGateCoreDispatchMiddleware(INetworkConnectivityService? networkService = null)
    {
        _networkService = networkService;
    }

    public async Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        var title = BridgeInit.DeriveSessionTitle(ctx.Options);
        ctx.Title = title;

        var baseUrl = ctx.GetBaseUrl();
        ctx.BaseUrl = baseUrl;

        if (ctx.HttpClient is not { } httpClient || ctx.TransportFactory is not { } transportFactory)
        {
            ctx.Logger?.LogError("Bridge: httpClient or transportFactory not provided");
            ctx.Fail("httpClient or transportFactory not provided");
            return;
        }

        var orgUUID = ctx.OrgUUID ?? throw new InvalidOperationException("OrgUUID is not set. Ensure OrgUUIDFetchMiddleware runs first.");

        var useCcrV2 = BridgeRuntimeGate.IsCcrV2Enabled();

        await WaitForNetworkAsync(ctx, ct).ConfigureAwait(false);

        if (useCcrV2)
        {
            var envLessParams = new V2BridgeParams
            {
                BaseUrl = baseUrl,
                OrgUUID = orgUUID,
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

            ctx.Handle = await BridgeRemoteCore.InitV2BridgeCoreAsync(
                envLessParams, httpClient, transportFactory, ctx.V2Pipeline ?? throw new InvalidOperationException("V2Pipeline is not set."), ctx.Logger, ct).ConfigureAwait(false);
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
                    BridgeInit.CreateSessionViaApiAsync(baseUrl, token, envId, sessionTitle, httpClient, cts),
                ArchiveSession = (sid, cts) =>
                    BridgeSessionApi.ArchiveAsync(sid, baseUrl, ctx.GetAccessToken() ?? throw new InvalidOperationException("AccessToken is not available."),
                        orgUUID, 30000, httpClient, cts),
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
                coreParams, httpClient, ctx.FileSystem, transportFactory, ctx.V1Pipeline ?? throw new InvalidOperationException("V1Pipeline is not set."), ctx.Logger, ct).ConfigureAwait(false);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 等待网络恢复 — V1/V2 切换前确保网络可用,网络不可用时阻塞等待(带 30s 超时)
    /// </summary>
    private async Task WaitForNetworkAsync(BridgeInitGateContext ctx, CancellationToken ct)
    {
        if (_networkService is null) return;
        if (_networkService.IsNetworkAvailable()) return;

        ctx.Logger?.LogWarning("Bridge V1/V2 切换:网络不可用,等待恢复...");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<NetworkConnectivityChangedEventArgs> handler = (_, e) =>
        {
            if (e.CurrentState != NetworkConnectivityState.Offline) tcs.TrySetResult(true);
        };
        _networkService.StateChanged += handler;
        try
        {
            if (!_networkService.IsNetworkAvailable())
            {
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            ctx.Logger?.LogWarning("Bridge V1/V2 切换:等待网络恢复超时(30s),继续切换");
        }
        finally
        {
            _networkService.StateChanged -= handler;
        }

        ctx.Logger?.LogInformation("Bridge V1/V2 切换:网络已恢复");
    }
}
