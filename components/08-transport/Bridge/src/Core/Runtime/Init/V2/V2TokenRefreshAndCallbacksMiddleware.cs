
namespace Core.Bridge.Init.V2;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V2 JWT 刷新调度器 + 传输回调 + 连接 — 对齐 TS 端 §5-§8
/// </summary>
[Register]
internal sealed partial class V2TokenRefreshAndCallbacksMiddleware : IMiddleware<V2BridgeInitContext>
{

    public Task InvokeAsync(V2BridgeInitContext ctx, MiddlewareDelegate<V2BridgeInitContext> next, CancellationToken ct)
    {
        var sessionId = ctx.SessionId ?? throw new InvalidOperationException("SessionId is not set. Ensure TokenValidationMiddleware runs first.");
        var credentials = ctx.Credentials ?? throw new InvalidOperationException("Credentials is not set. Ensure TokenValidationMiddleware runs first.");
        var transport = ctx.Transport ?? throw new InvalidOperationException("Transport is not set. Ensure TransportConnectMiddleware runs first.");

        var config = ctx.Config;
        var parameters = ctx.Parameters;
        var state = new BridgeInitState
        {
            FlushGate = new BridgeFlushGate<string>(),
            RecentPostedUUIDs = new BoundedUUIDSet(config.UuidDedupBufferSize),
            RecentInboundUUIDs = new BoundedUUIDSet(config.UuidDedupBufferSize),
            InitCts = new CancellationTokenSource(),
            UserMessageCallbackDone = parameters.OnUserMessage is null,
        };

        // 对齐 TS 端: initialMessageUUIDs
        if (parameters.InitialMessages is { Length: > 0 })
        {
            state.InitialMessageUUIDs = new BoundedUUIDSet(config.UuidDedupBufferSize);
            foreach (var msg in parameters.InitialMessages)
            {
                var uuid = BridgeMessaging.ExtractUuid(msg);
                if (uuid is not null)
                {
                    state.InitialMessageUUIDs.Add(uuid);
                }
            }
        }

        ctx.State = state;

        // JWT 刷新调度器 — 对齐 TS 端 §5
        var refresh = new BridgeTokenRefreshScheduler(
            new TokenRefreshOptions
            {
                GetAccessToken = () => parameters.GetAccessToken(),
                OnRefresh = (sid, oauthToken) =>
                {
                    if (state.AuthRecoveryInFlight || state.TornDown)
                    {
                        ctx.Logger?.LogDebug("Bridge: Recovery already in flight, skipping proactive refresh");
                        return;
                    }

                    ctx.Logger?.LogDebug("Bridge: Token refresh triggered for session {SessionId}", sid);
                    parameters.OnStateChange?.Invoke(BridgeState.Reconnecting, "Proactive token refresh");
                },
                Label = "remote",
                RefreshBufferMs = config.TokenRefreshBufferMs,
                Logger = ctx.Logger,
            });

        refresh.ScheduleFromExpiresIn(sessionId, credentials.ExpiresIn);
        ctx.Refresh = refresh;

        // 传输回调 — 对齐 TS 端 §6
        BridgeRemoteCore.WireTransportCallbacks(transport, sessionId, parameters, config, ctx.Logger,
            state, ctx.TransportFactory, refresh, ct);

        // 连接传输
        if (parameters.InitialMessages is { Length: > 0 })
        {
            state.FlushGate.Start();
        }
        transport.Connect();

        // 设置全局桥句柄
        var handle = new EnvLessBridgeHandle(
            new BridgeEnvLessSessionContext
            {
                Session = new BridgeSessionInfo { SessionId = sessionId, EnvironmentId = string.Empty, SessionIngressUrl = credentials.ApiBaseUrl },
                State = state,
                Parameters = parameters,
            },
            new BridgeEnvLessTransportContext
            {
                Transport = transport,
                HttpClient = ctx.HttpClient,
                Config = config,
                Refresh = refresh,
            },
            ctx.Logger);
        BridgeHandle.SetHandle(handle);
        ctx.Handle = handle;

        return next(ctx, ct);
    }
}
