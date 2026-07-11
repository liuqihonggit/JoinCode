
namespace Core.Bridge.Init.V1;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// V1 设置工作轮询循环 + 传输回调 — 对齐 TS 端 §7-§10
/// 这是 V1 初始化最复杂的步骤，包含 lambda 回调和事件订阅
/// </summary>
[Register]
internal sealed class V1WorkPollSetupMiddleware : IMiddleware<V1BridgeInitContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(V1BridgeInitContext ctx, MiddlewareDelegate<V1BridgeInitContext> next, CancellationToken ct)
    {
        var parameters = ctx.Parameters;
        var fs = ctx.FileSystem;
        var logger = ctx.Logger;
        var apiClient = ctx.ApiClient!;

        // 初始化去重集合和状态
        var recentPostedUUIDs = new BoundedUUIDSet(2000);
        var recentInboundUUIDs = new BoundedUUIDSet(2000);

        BoundedUUIDSet? initialMessageUUIDs = null;
        if (parameters.InitialMessages is { Length: > 0 })
        {
            initialMessageUUIDs = new BoundedUUIDSet(2000);
            foreach (var msg in parameters.InitialMessages)
            {
                var uuid = BridgeMessaging.ExtractUuid(msg);
                if (uuid is not null)
                {
                    initialMessageUUIDs.Add(uuid);
                }
            }
        }

        var state = new BridgeInitState
        {
            FlushGate = new BridgeFlushGate<string>(),
            RecentPostedUUIDs = recentPostedUUIDs,
            RecentInboundUUIDs = recentInboundUUIDs,
            InitialMessageUUIDs = initialMessageUUIDs,
            InitCts = new CancellationTokenSource(),
            UserMessageCallbackDone = parameters.OnUserMessage is null,
        };

        ctx.State = state;

        // 创建工作轮询循环
        var pollConfig = parameters.GetPollIntervalConfig?.Invoke();
        var pollOptions = new BridgeWorkPollOptions
        {
            IdlePollIntervalMs = pollConfig?.PollIntervalMsNotAtCapacity ?? 5000,
            AtCapacityPollIntervalMs = pollConfig?.PollIntervalMsAtCapacity ?? 30000,
        };

        var pollLoop = new BridgeWorkPollLoop(apiClient, pollOptions, logger);
        ctx.PollLoop = pollLoop;

        // 订阅工作接收事件
        IReplBridgeTransport? currentTransport = null;
        var getOAuthToken = parameters.GetAccessToken;
        int v2Generation = 0;
        var sessionId = ctx.SessionId!;
        var environmentId = ctx.EnvironmentId!;
        var environmentSecret = ctx.EnvironmentSecret!;
        var sessionIngressUrl = ctx.SessionIngressUrl!;
        var transportFactory = ctx.TransportFactory;

        pollLoop.WorkReceived += (_, e) =>
        {
            if (state.TornDown) return;

            if (!string.IsNullOrEmpty(e.SessionId) && e.SessionId != sessionId)
            {
                logger?.LogWarning("Bridge v1: 收到外部会话工作项 (expected={Expected} got={Got})，跳过",
                    sessionId, e.SessionId);
                return;
            }

            var useCcrV2 = e.UseCcrV2;

            string? oauthToken = null;
            if (!useCcrV2)
            {
                oauthToken = getOAuthToken();
                if (string.IsNullOrEmpty(oauthToken))
                {
                    logger?.LogDebug("Bridge v1: 无 OAuth token，跳过工作");
                    return;
                }
            }

            // 刷新指针 mtime — best-effort
            try
            {
                var pointerService = new BridgePointerService(fs, logger);
                _ = pointerService.WriteAsync(parameters.Dir, new BridgePointer
                {
                    SessionId = sessionId,
                    EnvironmentId = environmentId,
                    Source = BridgePointerSource.Repl.ToValue(),
                }, ct);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[BridgeRemoteCore] Write pointer during reconnect failed: {ex.Message}"); }

            // 关闭旧传输
            if (currentTransport is not null)
            {
                var oldTransport = currentTransport;
                currentTransport = null;
                var oldSeq = oldTransport.GetLastSequenceNum();
                if (oldSeq > state.LastTransportSequenceNum)
                {
                    state.LastTransportSequenceNum = oldSeq;
                }
                try { oldTransport.Close(); } catch (Exception ex2) { System.Diagnostics.Trace.WriteLine($"[BridgeRemoteCore] Close old transport failed: {ex2.Message}"); }
                _ = oldTransport.DisposeAsync();
            }

            state.FlushGate.Deactivate();
            v2Generation++;

            if (useCcrV2)
            {
                // v2 路径
                var sessionUrl = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl(
                    e.ApiBaseUrl ?? parameters.BaseUrl, e.SessionId);
                var thisGen = v2Generation;

                logger?.LogInformation("Bridge v1: CCR v2 路径: sessionUrl={Url}, session={Session}, gen={Gen}",
                    sessionUrl, e.SessionId, thisGen);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var epoch = await BridgeWorkSecretDecoder.RegisterWorkerAsync(
                            sessionUrl, e.IngressToken ?? "", apiClient.HttpClient, ct).ConfigureAwait(false);

                        if (state.TornDown || ct.IsCancellationRequested)
                        {
                            logger?.LogDebug("Bridge v1: CCR v2 握手期间拆卸已启动，丢弃传输");
                            return;
                        }

                        if (thisGen != v2Generation)
                        {
                            logger?.LogDebug("Bridge v1: CCR v2 丢弃过时握手 gen={Gen} current={Current}",
                                thisGen, v2Generation);
                            return;
                        }

                        var v2Transport = transportFactory.CreateV2Transport(new V2TransportOptions
                        {
                            SseUrl = $"{sessionUrl}/worker/events/stream",
                            ApiBaseUrl = sessionUrl,
                            IngressToken = e.IngressToken ?? "",
                            SessionId = e.SessionId,
                            Epoch = (int)epoch,
                            InitialSequenceNum = state.LastTransportSequenceNum,
                        }, logger);

                        currentTransport = v2Transport;
                        pollLoop.SetTransport(v2Transport);

                        BridgeRemoteCore.WireV2TransportCallbacks(v2Transport, sessionId, parameters, state, pollLoop, logger, ct);

                        v2Transport.SetOnBatchDropped((batchSize, failures) =>
                        {
                            logger?.LogWarning("Bridge v2: 批次丢弃（{BatchSize}条，{Failures}次失败）— Lost sync", batchSize, failures);
                            parameters.OnStateChange?.Invoke(BridgeState.Reconnecting,
                                "Lost sync with Remote Control — events could not be delivered");
                            pollLoop.Wake();
                        });

                        if (!state.InitialFlushDone && parameters.InitialMessages is { Length: > 0 })
                        {
                            state.FlushGate.Start();
                        }

                        v2Transport.Connect();
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Bridge v1: CCR v2 创建传输失败");
                        if (thisGen != v2Generation) return;
                        try
                        {
                            await apiClient.StopWorkAsync(environmentId, e.WorkId, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex2) { System.Diagnostics.Trace.WriteLine($"[BridgeRemoteCore] StopWork after transport creation failed: {ex2.Message}"); }
                        pollLoop.Wake();
                    }
                }, ct);
            }
            else
            {
                // v1 路径
                var wsUrl = BridgeWorkSecretDecoder.BuildSdkUrl(sessionIngressUrl, e.SessionId);
                var postUrl = BridgeRemoteCore.ConvertWsUrlToPostUrl(wsUrl);

                var transport = transportFactory.CreateV1Transport(new V1TransportOptions
                {
                    WebSocketEndpoint = wsUrl,
                    PostEndpoint = postUrl,
                    AuthHeader = $"Bearer {oauthToken}",
                    RefreshHeaders = () =>
                    {
                        var fresh = getOAuthToken();
                        return fresh is not null ? $"Bearer {fresh}" : null;
                    },
                    MaxConsecutiveFailures = 50,
                }, logger);

                currentTransport = transport;
                pollLoop.SetTransport(transport);

                BridgeRemoteCore.WireV1TransportCallbacks(transport, sessionId, parameters, state, pollLoop, logger, ct);

                transport.SetOnBatchDropped((batchSize, failures) =>
                {
                    logger?.LogWarning("Bridge v1: 批次丢弃（{BatchSize}条，{Failures}次失败）— Lost sync", batchSize, failures);
                    parameters.OnStateChange?.Invoke(BridgeState.Reconnecting,
                        "Lost sync with Remote Control — events could not be delivered");
                    pollLoop.Wake();
                });

                if (!state.InitialFlushDone && parameters.InitialMessages is { Length: > 0 })
                {
                    state.FlushGate.Start();
                }

                transport.Connect();
            }
        };

        // 订阅心跳致命错误
        pollLoop.HeartbeatFatal += (_, e) =>
        {
            if (state.TornDown) return;

            logger?.LogWarning("Bridge v1: 心跳致命错误 (status={Status})，清理工作状态", e.Exception.Message);

            if (currentTransport is not null)
            {
                var seq = currentTransport.GetLastSequenceNum();
                if (seq > state.LastTransportSequenceNum)
                {
                    state.LastTransportSequenceNum = seq;
                }
                try { currentTransport.Close(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[BridgeRemoteCore] Close transport during teardown failed: {ex.Message}"); }
                _ = currentTransport.DisposeAsync();
                currentTransport = null;
            }

            state.FlushGate.Drop();
            pollLoop.Wake();
            parameters.OnStateChange?.Invoke(BridgeState.Reconnecting,
                "Work item lease expired, fetching fresh token");
        };

        // 启动轮询循环
        if (!pollLoop.StartWithExistingEnvironment(environmentId, environmentSecret, ct))
        {
            ctx.Fail("Poll loop start failed");
            return Task.CompletedTask;
        }

        // 创建并返回句柄
        var handle = new V1BridgeHandle(
            new BridgeSessionInfo { SessionId = sessionId, EnvironmentId = environmentId, SessionIngressUrl = sessionIngressUrl },
            new BridgeCoreContext { PollLoop = pollLoop, Parameters = parameters },
            new BridgeTransportContext { HttpClient = ctx.HttpClient, ApiClient = apiClient },
            state, fs, logger);
        BridgeHandle.SetHandle(handle);
        ctx.Handle = handle;

        return next(ctx, ct);
    }
}
