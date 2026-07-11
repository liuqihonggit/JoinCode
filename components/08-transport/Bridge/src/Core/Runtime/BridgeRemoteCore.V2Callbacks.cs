
namespace Core.Bridge;

public static partial class BridgeRemoteCore
{
    /// <summary>
    /// 注册 v2 传输回调 — 对齐 TS 端 wireTransport (v2 路径)
    /// 与 v1 的区别: 跳过 OAuth token 更新（v2 使用 JWT，覆盖会破坏 /worker/* 请求的 session_id 校验）
    /// </summary>
    internal static void WireV2TransportCallbacks(
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

            // 陈旧传输守卫
            if (pollLoop.CurrentTransport != transport) return;

            logger?.LogInformation("Bridge v2: 传输已连接");

            // v2 跳过 OAuth token 更新 — 对齐 TS 端: if (!useCcrV2) { updateSessionIngressAuthToken(freshToken) }
            // JWT 已在 V2ReplBridgeTransport 构造时存储，覆盖会破坏 /worker/* 请求的 session_id 校验

            // 重置 TeardownStarted — 对齐 TS 端: teardownStarted = false
            state.TeardownStarted = false;

            // 初始消息刷新
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
                        logger?.LogError("Bridge v2: flushHistory 失败: {Message}",
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
            // 陈旧传输守卫
            if (pollLoop.CurrentTransport != transport) return;

            logger?.LogWarning("Bridge v2: 传输永久关闭 (code={Code})", code);

            var closedSeq = transport.GetLastSequenceNum();
            if (closedSeq > state.LastTransportSequenceNum)
            {
                state.LastTransportSequenceNum = closedSeq;
            }

            pollLoop.ClearTransport();
            pollLoop.Wake();

            var dropped = state.FlushGate.Drop();
            if (dropped > 0)
            {
                logger?.LogDebug("Bridge v2: 传输关闭时丢弃 {Count} 条排队消息 (code={Code})",
                    dropped, code);
            }

            if (code == 1000)
            {
                parameters.OnStateChange?.Invoke(BridgeState.Failed, "session ended");
                return;
            }

            parameters.OnStateChange?.Invoke(BridgeState.Reconnecting,
                $"Transport closed (code {code}), reconnecting...");

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
                            logger?.LogError("Bridge v2: 环境重连失败");
                            parameters.OnStateChange?.Invoke(BridgeState.Failed, "Reconnect failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Bridge v2: 重连异常");
                    if (!state.TornDown)
                    {
                        parameters.OnStateChange?.Invoke(BridgeState.Failed, $"Reconnect error: {ex.Message}");
                    }
                }
            }, state.InitCts.Token);
        });
    }

    /// <summary>
    /// 将 WebSocket URL 转换为 HTTP POST 端点 URL — 对齐 TS 端 convertWsUrlToPostUrl
    /// wss://api.example.com/v2/session_ingress/ws/{session_id}
    /// → https://api.example.com/v2/session_ingress/session/{session_id}/events
    /// </summary>
    internal static string ConvertWsUrlToPostUrl(string wsUrl)
    {
        var uri = new Uri(wsUrl);
        var protocol = uri.Scheme == "wss" ? "https" : "http";

        // 替换 /ws/ 为 /session/ 并追加 /events
        var path = uri.AbsolutePath;
        var wsIndex = path.IndexOf("/ws/", StringComparison.Ordinal);
        if (wsIndex >= 0)
        {
            path = string.Concat(path.AsSpan(0, wsIndex), "/session/", path.AsSpan(wsIndex + 4));
        }

        if (!path.EndsWith("/events", StringComparison.Ordinal))
        {
            path = path.EndsWith('/') ? path + "events" : path + "/events";
        }

        return $"{protocol}://{uri.Host}{path}";
    }

    /// <summary>
    /// 环境重连双策略 — 对齐 TS 端 reconnectEnvironmentWithSession
    /// Strategy 1: reconnect-in-place — 同一环境+同一会话，调用 /bridge/reconnect
    /// Strategy 2: fresh session — 归档旧会话，在新环境上创建新会话
    /// </summary>
    internal static async Task<bool> ReconnectEnvironmentWithSessionAsync(
        string sessionId,
        string environmentId,
        BridgeCoreParams parameters,
        BridgeApiClient apiClient,
        HttpClient httpClient,
        BridgeInitState state,
        BridgeWorkPollLoop pollLoop,
        V1ReconnectState reconnectState,
        IFileSystem fs,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        reconnectState.EnvironmentRecreations++;

        if (reconnectState.EnvironmentRecreations > state.MaxEnvironmentRecreations)
        {
            logger?.LogError("Bridge v1: 环境重连次数耗尽 ({Max})", state.MaxEnvironmentRecreations);
            return false;
        }

        logger?.LogInformation("Bridge v1: 环境重连 (第 {Count}/{Max} 次)",
            reconnectState.EnvironmentRecreations, state.MaxEnvironmentRecreations);

        // 对齐 TS 端 doReconnect:
        // 1. 释放当前工作项 (force=false → 服务器重新入队)
        if (pollLoop.CurrentTransport is not null)
        {
            var seq = pollLoop.CurrentTransport.GetLastSequenceNum();
            if (seq > state.LastTransportSequenceNum)
            {
                state.LastTransportSequenceNum = seq;
            }
            try { pollLoop.CurrentTransport.Close(); } catch (Exception ex) { /* 忽略 */ System.Diagnostics.Trace.WriteLine($"[BridgeRemoteCore] Close transport during reconnect failed: {ex.Message}"); }
            _ = pollLoop.CurrentTransport.DisposeAsync();
            pollLoop.ClearTransport();
        }

        // 对齐 TS 端: stopWork(force=false) — 让服务器将工作项重新入队
        var workIdBeforeStop = pollLoop.CurrentWorkId;
        try
        {
            await pollLoop.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Bridge v1: 重连前 stopWork 失败（非致命）");
        }

        pollLoop.Wake();
        state.FlushGate.Drop();

        // 对齐 TS 端: 检查点 0 — stopWork 后检查 currentWorkId 是否变化
        // 如果轮询循环在 stopWork await 期间自行恢复了（onWorkReceived 触发），
        // 说明已有新工作项，不需要继续归档会话
        if (pollLoop.CurrentWorkId is not null && pollLoop.CurrentWorkId != workIdBeforeStop)
        {
            logger?.LogInformation("Bridge v1: stopWork 期间轮询循环自行恢复 (workId: {Old} → {New})，跳过重连",
                workIdBeforeStop, pollLoop.CurrentWorkId);
            return true;
        }

        // 对齐 TS 端: 检查点 1 — stopWork 后检查是否已被 teardown 中止
        if (state.TornDown || ct.IsCancellationRequested)
        {
            logger?.LogDebug("Bridge v1: Reconnect aborted by teardown after stopWork");
            return false;
        }

        // 2. 重新注册环境 — 对齐 TS 端: api.registerBridgeEnvironment(bridgeConfig)
        // 传递 reuseEnvironmentId 让服务器尝试复活同一环境
        var bridgeConfig = new BridgeEnvironmentRegistration
        {
            BridgeId = Guid.NewGuid().ToString("N"),
            MachineName = parameters.MachineName,
            Dir = parameters.Dir,
            Branch = parameters.Branch,
            GitRepoUrl = parameters.GitRepoUrl,
            WorkerType = parameters.WorkerType,
            MaxSessions = 1,
            ReuseEnvironmentId = environmentId,
        };

        BridgeEnvironmentRegistrationResponse? regResponse;
        try
        {
            regResponse = await apiClient.RegisterBridgeEnvironmentAsync(bridgeConfig, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Bridge v1: 环境重注册失败");
            return false;
        }

        if (regResponse is null)
        {
            logger?.LogError("Bridge v1: 环境重注册返回 null");
            return false;
        }

        reconnectState.EnvironmentId = regResponse.EnvironmentId;
        reconnectState.EnvironmentSecret = regResponse.BridgeId;

        // 对齐 TS 端: 检查点 2 — 环境注册后检查中止
        if (state.TornDown || ct.IsCancellationRequested)
        {
            logger?.LogDebug("Bridge v1: Reconnect aborted after env registration, cleaning up");
            try { await apiClient.DeregisterEnvironmentAsync(reconnectState.EnvironmentId, ct).ConfigureAwait(false); } catch (Exception ex2) { System.Diagnostics.Trace.WriteLine($"[BridgeRemoteCore] Deregister environment during abort failed: {ex2.Message}"); }
            return false;
        }

        logger?.LogInformation("Bridge v1: 环境重注册成功: requested={Requested} got={Got}",
            environmentId, reconnectState.EnvironmentId);

        // 3. Strategy 1: reconnect-in-place — 对齐 TS 端 tryReconnectInPlace
        // 仅当环境 ID 不变时尝试（同一环境可复活）
        if (reconnectState.EnvironmentId == environmentId)
        {
            try
            {
                var result = await BridgeSessionApi.ReconnectSessionAsync(
                    reconnectState.EnvironmentId, sessionId, httpClient, ct).ConfigureAwait(false);
                if (result is not null)
                {
                    logger?.LogInformation("Bridge v1: 会话原地重连成功: {SessionId}", sessionId);
                    reconnectState.EnvironmentRecreations = 0;
                    return true;
                }
                logger?.LogWarning("Bridge v1: 会话原地重连返回 null");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Bridge v1: 原地重连失败，尝试创建新会话");
            }
        }

        // 4. Strategy 2: fresh session — 对齐 TS 端 archiveSession + createSession
        // 归档旧会话
        try
        {
            await parameters.ArchiveSession(sessionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Bridge v1: 归档旧会话失败（非致命）");
        }

        // 对齐 TS 端: 检查点 3 — 归档后检查中止
        if (state.TornDown || ct.IsCancellationRequested)
        {
            logger?.LogDebug("Bridge v1: Reconnect aborted after archive");
            return false;
        }

        // 创建新会话
        var accessToken = parameters.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            logger?.LogError("Bridge v1: 重连时无 OAuth token");
            return false;
        }

        string? newSessionId;
        try
        {
            var currentTitle = parameters.GetCurrentTitle?.Invoke() ?? parameters.Title;
            newSessionId = await parameters.CreateSession(
                reconnectState.EnvironmentId, currentTitle, parameters.GitRepoUrl, accessToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Bridge v1: 重连时创建新会话失败");
            return false;
        }

        if (string.IsNullOrEmpty(newSessionId))
        {
            logger?.LogError("Bridge v1: 重连时创建新会话返回空");
            return false;
        }

        // 更新会话 ID 和崩溃恢复指针
        reconnectState.SessionId = newSessionId;

        // 对齐 TS 端: 检查点 4 — 新会话创建后检查中止
        if (state.TornDown || ct.IsCancellationRequested)
        {
            logger?.LogDebug("Bridge v1: Reconnect aborted after session creation, archiving new session");
            try { await parameters.ArchiveSession(newSessionId, ct).ConfigureAwait(false); } catch (Exception ex) { /* best-effort */ System.Diagnostics.Trace.WriteLine($"[BridgeRemoteCore] Archive session during abort failed: {ex.Message}"); }
            return false;
        }

        // 重置传输状态 — 对齐 TS 端: lastTransportSequenceNum = 0, recentInboundUUIDs.clear()
        state.LastTransportSequenceNum = 0;

        // 写入崩溃恢复指针
        try
        {
            var pointerService = new BridgePointerService(fs, logger);
            await pointerService.WriteAsync(parameters.Dir, new BridgePointer
            {
                SessionId = newSessionId,
                EnvironmentId = reconnectState.EnvironmentId,
                Source = BridgePointerSource.Repl.ToValue(),
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Bridge v1: 重连时写入崩溃恢复指针失败");
        }

        reconnectState.EnvironmentRecreations = 0;

        // 对齐 TS 端: previouslyFlushedUUIDs.clear() — Strategy 2 后清除，让初始消息重新发送到新会话
        // UUID 在服务器端按会话隔离，重新 flush 是安全的
        if (parameters.PreviouslyFlushedUUIDs is not null)
        {
            parameters.PreviouslyFlushedUUIDs.Clear();
        }

        // 对齐 TS 端: userMessageCallbackDone = !onUserMessage — Strategy 2 后重置闩锁
        // 新会话需要重新派生标题（旧会话的 PATCH 已随归档丢失）
        // 自修正: 如果策略已完成（显式标题或 count>=3），回调立即返回 true 重新闩锁
        state.UserMessageCallbackDone = parameters.OnUserMessage is null;

        // 对齐 TS 端: reconnect 创建新会话后更新 PID 文件中的 bridgeSessionId
        // setReplBridgeHandle 只在 init/teardown 触发，reconnect 需要单独更新
        var compatId = SessionIdCompat.ToCompatSessionId(newSessionId);
        parameters.ConcurrentSessionService?.UpdateBridgeSessionIdAsync(compatId, ct).ConfigureAwait(false);

        logger?.LogInformation("Bridge v1: 新会话已创建: {SessionId}", newSessionId);
        return true;
    }
}
