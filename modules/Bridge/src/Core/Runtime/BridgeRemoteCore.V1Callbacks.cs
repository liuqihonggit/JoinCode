
namespace Core.Bridge;

public static partial class BridgeRemoteCore
{
    /// <summary>
    /// 注册 v1 传输回调 — 对齐 TS 端 wireTransport
    /// </summary>
    internal static void WireV1TransportCallbacks(
        IReplBridgeTransport transport,
        string sessionId,
        BridgeCoreParams parameters,
        BridgeInitState state,
        BridgeWorkPollLoop pollLoop,
        ILogger? logger,
        CancellationToken ct)
    {
        transport.SetOnConnect(() =>
        {
            if (state.TornDown) return;

            // 陈旧传输守卫 — 对齐 TS 端: if (transport !== currentTransport) return
            if (pollLoop.CurrentTransport != transport) return;

            logger?.LogInformation("Bridge v1: 传输已连接");

            // v1 专属: 更新 OAuth token 到环境变量 — 对齐 TS 端 updateSessionIngressAuthToken
            // v2 跳过此步（v2 在 createV2ReplTransport 中已存储 JWT，覆盖会破坏 /worker/* 请求的 session_id 校验）
            var currentToken = parameters.GetAccessToken();
            if (!string.IsNullOrEmpty(currentToken))
            {
                Environment.SetEnvironmentVariable(
                    JccEnvVar.SessionAccessToken.ToValue(), currentToken);
            }

            // 初始消息刷新 — 对齐 TS 端: if (!initialFlushDone && initialMessages)
            if (!state.InitialFlushDone && parameters.InitialMessages is { Length: > 0 })
            {
                state.InitialFlushDone = true;
                _ = FlushHistoryAsync(
                    parameters.InitialMessages,
                    parameters.InitialHistoryCap,
                    parameters.ToSDKMessages,
                    transport,
                    sessionId,
                    state.InitCts.Token,
                    parameters.PreviouslyFlushedUUIDs)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        logger?.LogError("Bridge v1: flushHistory 失败: {Message}",
                            task.Exception?.InnerException?.Message);
                    }

                    if (state.TornDown) return;
                    DrainFlushGate(state.FlushGate, state.RecentPostedUUIDs,
                        parameters.ToSDKMessages, transport, sessionId, state.InitCts.Token);
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
            // 陈旧传输守卫 — 对齐 TS 端: if (transport !== currentTransport) return
            if (pollLoop.CurrentTransport != transport) return;

            logger?.LogWarning("Bridge v1: 传输永久关闭 (code={Code})", code);

            // 对齐 TS 端 handleTransportPermanentClose:
            // 1. 捕获 SSE 序列号高水位
            var closedSeq = transport.GetLastSequenceNum();
            if (closedSeq > state.LastTransportSequenceNum)
            {
                state.LastTransportSequenceNum = closedSeq;
            }

            // 2. 清空传输引用 — 对齐 TS 端: transport = null
            pollLoop.ClearTransport();

            // 3. 唤醒轮询循环 — 对齐 TS 端: wakePollLoop()
            pollLoop.Wake();

            // 4. 丢弃 flushGate 中排队的消息 — 对齐 TS 端: flushGate.drop()
            var dropped = state.FlushGate.Drop();
            if (dropped > 0)
            {
                logger?.LogDebug("Bridge v1: 传输关闭时丢弃 {Count} 条排队消息 (code={Code})",
                    dropped, code);
            }

            // 5. 根据关闭码决定行为 — 对齐 TS 端
            if (code == 1000)
            {
                // 干净关闭 — 会话正常结束，触发拆卸
                parameters.OnStateChange?.Invoke(BridgeState.Failed, "session ended");
                return;
            }

            // 非干净关闭 — 标记重连中，尝试环境重连
            // 对齐 TS 端: onStateChange?.('reconnecting') + reconnectEnvironmentWithSession()
            parameters.OnStateChange?.Invoke(BridgeState.Reconnecting,
                $"Transport closed (code {code}), reconnecting...");

            // 异步触发重连 — 对齐 TS 端: void reconnectEnvironmentWithSession()
            _ = Task.Run(async () =>
            {
                try
                {
                    var handle = BridgeHandle.GetHandle() as V1BridgeHandle;
                    if (handle is not null)
                    {
                        var reconnected = await handle.ReconnectAsync(state.InitCts.Token).ConfigureAwait(false);
                        if (!reconnected && !state.TornDown)
                        {
                            logger?.LogError("Bridge v1: 环境重连失败");
                            parameters.OnStateChange?.Invoke(BridgeState.Failed, "Reconnect failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Bridge v1: 重连异常");
                    if (!state.TornDown)
                    {
                        parameters.OnStateChange?.Invoke(BridgeState.Failed, $"Reconnect error: {ex.Message}");
                    }
                }
            }, state.InitCts.Token);
        });
    }
}
