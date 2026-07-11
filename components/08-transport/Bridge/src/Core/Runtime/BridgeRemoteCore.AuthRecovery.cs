
namespace Core.Bridge;

public static partial class BridgeRemoteCore
{
    #region recoverFromAuthFailure

    /// <summary>
    /// 401 认证恢复 — 对齐 TS 端 recoverFromAuthFailure
    /// SSE 401 时自动刷新 OAuth → 重新获取凭证 → 重建传输
    /// </summary>
    internal static async Task RecoverFromAuthFailureAsync(
        string sessionId,
        BridgeEnvLessParams parameters,
        HttpClient httpClient,
        BridgeEnvLessConfig config,
        ILogger? logger,
        IReplBridgeTransport oldTransport,
        IReplBridgeTransportFactory transportFactory,
        BridgeInitState state,
        BridgeTokenRefreshScheduler refresh,
        CancellationToken ct)
    {
        // 对齐 TS 端: if (authRecoveryInFlight) return — 防止并发恢复
        if (state.AuthRecoveryInFlight) return;
        state.AuthRecoveryInFlight = true;
        parameters.OnStateChange?.Invoke(BridgeState.Reconnecting, "JWT expired — refreshing");

        try
        {
            // 对齐 TS 端: 先尝试 OAuth 刷新
            var stale = parameters.GetAccessToken();
            if (parameters.OnAuth401 is not null)
            {
                await parameters.OnAuth401(stale ?? string.Empty).ConfigureAwait(false);
            }

            var oauthToken = parameters.GetAccessToken() ?? stale;
            if (string.IsNullOrEmpty(oauthToken) || state.TornDown)
            {
                if (!state.TornDown)
                {
                    parameters.OnStateChange?.Invoke(BridgeState.Failed, "JWT refresh failed: no OAuth token");
                }
                return;
            }

            // 对齐 TS 端: withRetry(fetchRemoteCredentials)
            var fresh = await WithRetryAsync(
                () => FetchCredentialsWithDeviceTokenAsync(
                    sessionId, parameters, config.HttpTimeoutMs, httpClient, oauthToken, ct),
                "fetchRemoteCredentials (recovery)",
                config.InitRetryMaxAttempts,
                config.InitRetryBaseDelayMs,
                config.InitRetryMaxDelayMs,
                config.InitRetryJitterFraction,
                ct).ConfigureAwait(false);

            if (fresh is null || state.TornDown)
            {
                if (!state.TornDown)
                {
                    parameters.OnStateChange?.Invoke(BridgeState.Failed, "JWT refresh failed after 401");
                }
                return;
            }

            // 对齐 TS 端: initialFlushDone = false — 401 中断初始刷新时重置
            state.InitialFlushDone = false;

            // 对齐 TS 端: rebuildTransport(fresh, 'auth_401_recovery')
            await RebuildTransportAsync(
                fresh, sessionId, parameters, config, logger,
                oldTransport, transportFactory, state, refresh, ct).ConfigureAwait(false);

            logger?.LogDebug("Bridge: Transport rebuilt after 401");
        }
        catch (Exception ex)
        {
            logger?.LogError("Bridge: 401 recovery failed: {Message}", ex.Message);
            if (!state.TornDown)
            {
                parameters.OnStateChange?.Invoke(BridgeState.Failed, $"JWT refresh failed: {ex.Message}");
            }
        }
        finally
        {
            state.AuthRecoveryInFlight = false;
        }
    }

    /// <summary>
    /// 重建传输 — 对齐 TS 端 rebuildTransport
    /// 关闭旧传输 → 创建新传输 → 重新注册回调 → 连接
    /// </summary>
    internal static async Task RebuildTransportAsync(
        BridgeRemoteCredentials fresh,
        string sessionId,
        BridgeEnvLessParams parameters,
        BridgeEnvLessConfig config,
        ILogger? logger,
        IReplBridgeTransport oldTransport,
        IReplBridgeTransportFactory transportFactory,
        BridgeInitState state,
        BridgeTokenRefreshScheduler refresh,
        CancellationToken ct)
    {
        // 对齐 TS 端: flushGate.start() — 排队写入消息
        state.FlushGate.Start();

        try
        {
            // 对齐 TS 端: 保存序列号 + 关闭旧传输
            var seq = oldTransport.GetLastSequenceNum();
            oldTransport.Close();

            // 对齐 TS 端: 创建新传输
            var sdkUrl = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl(fresh.ApiBaseUrl, sessionId);
            var newTransport = transportFactory.CreateV2Transport(sdkUrl, sessionId, fresh.WorkerJwt, config.ConnectTimeoutMs);

            if (state.TornDown)
            {
                newTransport.Close();
                return;
            }

            // 对齐 TS 端: wireTransportCallbacks + connect
            WireTransportCallbacks(newTransport, sessionId, parameters, config, logger,
                state, transportFactory, refresh, ct);

            newTransport.Connect();

            // 对齐 TS 端: refresh.scheduleFromExpiresIn
            refresh.ScheduleFromExpiresIn(sessionId, fresh.ExpiresIn);

            // 对齐 TS 端: drainFlushGate — 排空排队消息到新传输
            DrainFlushGate(state.FlushGate, state.RecentPostedUUIDs, parameters.ToSDKMessages, newTransport, sessionId, state.InitCts.Token);
        }
        finally
        {
            // 对齐 TS 端: flushGate.drop() — 失败路径也结束门控
            state.FlushGate.Drop();
        }
    }

    /// <summary>
    /// 注册传输回调 — 对齐 TS 端 wireTransportCallbacks
    /// 提取为独立方法以便 rebuildTransport 重新注册
    /// </summary>
    internal static void WireTransportCallbacks(
        IReplBridgeTransport transport,
        string sessionId,
        BridgeEnvLessParams parameters,
        BridgeEnvLessConfig config,
        ILogger? logger,
        BridgeInitState state,
        IReplBridgeTransportFactory transportFactory,
        BridgeTokenRefreshScheduler refresh,
        CancellationToken ct)
    {
        transport.SetOnConnect(() =>
        {
            if (!state.InitialFlushDone && parameters.InitialMessages is { Length: > 0 })
            {
                state.InitialFlushDone = true;
                _ = FlushHistoryAsync(
                    parameters.InitialMessages,
                    parameters.InitialHistoryCap,
                    parameters.ToSDKMessages,
                    transport,
                    sessionId,
                    state.InitCts.Token)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        logger?.LogError("Bridge: flushHistory failed: {Message}", task.Exception?.InnerException?.Message);
                    }

                    if (state.TornDown) return;
                    DrainFlushGate(state.FlushGate, state.RecentPostedUUIDs, parameters.ToSDKMessages, transport, sessionId, state.InitCts.Token);
                    parameters.OnStateChange?.Invoke(BridgeState.Connected, null);
                }, state.InitCts.Token);
            }
            else if (!state.FlushGate.Active)
            {
                parameters.OnStateChange?.Invoke(BridgeState.Connected, null);
            }
        });

        transport.SetOnData(data =>
        {
            BridgeMessaging.HandleIngressMessage(
                data,
                state.RecentPostedUUIDs,
                state.RecentInboundUUIDs,
                onInboundMessage: parameters.OnInboundMessage,
                onPermissionResponse: parameters.OnPermissionResponse,
                onControlRequest: async request =>
                {
                    var handlers = new ServerControlRequestHandlers
                    {
                        Transport = transport,
                        SessionId = sessionId,
                        OutboundOnly = parameters.OutboundOnly,
                        OnInterrupt = parameters.OnInterrupt,
                        OnSetModel = parameters.OnSetModel,
                        OnSetMaxThinkingTokens = parameters.OnSetMaxThinkingTokens,
                        OnSetPermissionMode = parameters.OnSetPermissionMode,
                    };
                    await BridgeMessaging.HandleServerControlRequestAsync(request, handlers, ct).ConfigureAwait(false);
                });
        });

        transport.SetOnClose(code =>
        {
            if (code == 401 && !state.AuthRecoveryInFlight)
            {
                _ = RecoverFromAuthFailureAsync(
                    sessionId, parameters, null!, config, logger,
                    transport, transportFactory, state, refresh, ct);
            }
            else if (code != 401)
            {
                parameters.OnStateChange?.Invoke(BridgeState.Failed, $"Transport closed (code {code})");
            }
        });
    }

    #endregion
}
