
namespace Core.Bridge;

/// <summary>
/// v1 env-based 桥句柄实现 — 对齐 TS 端 BridgeCoreHandle
/// </summary>
internal sealed class V1BridgeHandle : IReplBridgeHandle
{
    private readonly BridgeCoreContext _coreContext;
    private readonly BridgeTransportContext _transportContext;
    private readonly BridgeInitState _state;
    private readonly V1ReconnectState _reconnectState;
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _disposeCts = new();
    private volatile BridgeState _bridgeState;

    /// <summary>重连互斥 — 对齐 TS 端 reconnectPromise</summary>
    private Task<bool>? _reconnectTask;

    /// <summary>keep_alive 定时器 — 对齐 TS 端 keepAliveTimer (120s)</summary>
    private Timer? _keepAliveTimer;

    /// <summary>指针刷新定时器 — 对齐 TS 端 pointerRefreshTimer (perpetual 模式, 1h)</summary>
    private Timer? _pointerRefreshTimer;

    public string SessionId { get; }
    public string EnvironmentId { get; }
    public string SessionIngressUrl { get; }
    public BridgeState State => _bridgeState;

    public V1BridgeHandle(
        BridgeSessionInfo session,
        BridgeCoreContext coreContext,
        BridgeTransportContext transportContext,
        BridgeInitState state,
        IFileSystem fs,
        ILogger? logger = null)
    {
        SessionId = session.SessionId;
        EnvironmentId = session.EnvironmentId;
        SessionIngressUrl = session.SessionIngressUrl;
        _coreContext = coreContext;
        _transportContext = transportContext;
        _state = state;
        _fs = fs;
        _logger = logger;
        _reconnectState = new V1ReconnectState(session.EnvironmentId, string.Empty, session.SessionId);

        // 对齐 TS 端: keepAliveTimer — 每 120s 发送 keep_alive 帧防止代理 GC
        _keepAliveTimer = new Timer(_ =>
        {
            try
            {
                var transport = _coreContext.PollLoop.CurrentTransport;
                if (transport is not null && !_state.TornDown)
                {
                    var keepAliveJson = $"{{\"type\":\"keep_alive\",\"session_id\":\"{SessionId}\"}}";
                    _ = transport.WriteAsync(keepAliveJson, _disposeCts.Token);
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[V1BridgeHandle] Keep-alive failed: {ex.Message}"); }
        }, null, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120));

        // 对齐 TS 端: pointerRefreshTimer — perpetual 模式下每小时刷新指针 mtime
        if (_coreContext.Parameters.Perpetual)
        {
            _pointerRefreshTimer = new Timer(async _ =>
            {
                try
                {
                    // 对齐 TS 端: if (reconnectPromise) return
                    // doReconnect 非原子重赋值 sessionId/environmentId，定时器在此窗口写入会覆盖
                    // doReconnect 自身会写指针，跳过是安全的
                    if (_reconnectTask is not null) return;

                    var pointerService = new BridgePointerService(_fs, _logger);
                    await pointerService.WriteAsync(_coreContext.Parameters.Dir, new BridgePointer
                    {
                        SessionId = SessionId,
                        EnvironmentId = EnvironmentId,
                        Source = BridgePointerSource.Repl.ToValue(),
                    }, _disposeCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[V1BridgeHandle] Pointer refresh failed: {ex.Message}"); }
            }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }
    }

    /// <summary>写入消息 — 对齐 TS 端 writeMessages: FlushGate + dedup + titleDerivation + toSDKMessages</summary>
    public void WriteMessages(string[] messages)
    {
        if (_state.TornDown || messages.Length == 0) return;

        // 对齐 TS 端: 标题派生闩锁检查 — 在 flushGate 之前扫描
        // prompts 即使排队等待初始刷新也是标题候选
        if (!_state.UserMessageCallbackDone && _coreContext.Parameters.OnUserMessage is not null)
        {
            var onUserMessage = _coreContext.Parameters.OnUserMessage;
            foreach (var msg in messages)
            {
                var text = BridgeMessaging.ExtractTitleText(msg);
                if (text is not null && onUserMessage(text, SessionId))
                {
                    _state.UserMessageCallbackDone = true;
                    break;
                }
            }
        }

        // 对齐 TS 端: flushGate.enqueue() — 刷新期间排队消息
        if (_state.FlushGate.Enqueue(messages))
        {
            return;
        }

        var transport = _coreContext.PollLoop.CurrentTransport;
        if (transport is null) return;

        // 对齐 TS 端: 双层 UUID 去重 — initialMessageUUIDs + recentPostedUUIDs
        var filtered = FilterMessagesByUUID(messages);

        if (filtered.Count == 0) return;

        // 对齐 TS 端: toSDKMessages + writeBatch + session_id 注入
        if (_coreContext.Parameters.ToSDKMessages is not null)
        {
            var events = new List<string>(filtered.Count * 2);
            foreach (var msg in filtered)
            {
                var sdkMsgs = _coreContext.Parameters.ToSDKMessages(msg);
                foreach (var sdkMsg in sdkMsgs)
                {
                    events.Add(BridgeMessaging.InjectSessionId(sdkMsg, SessionId));
                }
            }

            if (events.Count > 0)
            {
                _ = transport.WriteBatchAsync(events, _disposeCts.Token);
            }
        }
        else
        {
            var events = new string[filtered.Count];
            for (var i = 0; i < filtered.Count; i++)
            {
                events[i] = BridgeMessaging.InjectSessionId(filtered[i], SessionId);
            }
            _ = transport.WriteBatchAsync(events, _disposeCts.Token);
        }
    }

    /// <summary>
    /// 双层 UUID 去重过滤 — 对齐 TS 端 writeMessages 中的 filter 逻辑
    /// 过滤掉已在 initialMessageUUIDs 或 recentPostedUUIDs 中的消息
    /// 发送后将新 UUID 添加到 recentPostedUUIDs
    /// </summary>
    private List<string> FilterMessagesByUUID(string[] messages)
    {
        var result = new List<string>(messages.Length);
        foreach (var msg in messages)
        {
            var uuid = BridgeMessaging.ExtractUuid(msg);
            if (uuid is not null)
            {
                // 对齐 TS 端: !initialMessageUUIDs.has(m.uuid) && !recentPostedUUIDs.has(m.uuid)
                if (_state.InitialMessageUUIDs?.Contains(uuid) == true ||
                    _state.RecentPostedUUIDs.Contains(uuid))
                {
                    continue;
                }
            }

            result.Add(msg);

            // 发送后添加到 recentPostedUUIDs — 对齐 TS 端: recentPostedUUIDs.add(m.uuid)
            if (uuid is not null)
            {
                _state.RecentPostedUUIDs.Add(uuid);
            }
        }

        return result;
    }

    /// <summary>写入 SDK 消息 — 对齐 TS 端 writeSdkMessages</summary>
    public void WriteSdkMessages(string[] messages)
    {
        if (_state.TornDown || messages.Length == 0) return;

        var transport = _coreContext.PollLoop.CurrentTransport;
        if (transport is null) return;

        _ = transport.WriteBatchAsync(messages, _disposeCts.Token);
    }

    public void SendControlRequest(string requestJson)
    {
        var transport = _coreContext.PollLoop.CurrentTransport;
        if (transport is null || _state.TornDown) return;
        if (_state.AuthRecoveryInFlight) return;

        _ = transport.WriteAsync(requestJson, _disposeCts.Token);
    }

    public void SendControlResponse(string responseJson)
    {
        var transport = _coreContext.PollLoop.CurrentTransport;
        if (transport is null || _state.TornDown) return;
        if (_state.AuthRecoveryInFlight) return;

        _ = transport.WriteAsync(responseJson, _disposeCts.Token);
    }

    public void SendControlCancelRequest(string requestId)
    {
        var transport = _coreContext.PollLoop.CurrentTransport;
        if (transport is null || _state.TornDown) return;
        if (_state.AuthRecoveryInFlight) return;

        var json = $"{{\"type\":\"cancel_control_request\",\"request_id\":\"{requestId}\"}}";
        _ = transport.WriteAsync(json, _disposeCts.Token);
    }

    public void SendResult()
    {
        var transport = _coreContext.PollLoop.CurrentTransport;
        if (transport is null || _state.TornDown) return;

        var json = $"{{\"type\":\"result\",\"session_id\":\"{SessionId}\"}}";
        _ = transport.WriteAsync(json, _disposeCts.Token);
    }

    /// <summary>环境重连 — 对齐 TS 端 reconnectEnvironmentWithSession</summary>
    public async Task<bool> ReconnectAsync(CancellationToken ct = default)
    {
        // 对齐 TS 端: if (reconnectPromise) return reconnectPromise
        if (_reconnectTask is not null)
        {
            return await _reconnectTask.ConfigureAwait(false);
        }

        _reconnectTask = DoReconnectAsync(ct);
        try
        {
            return await _reconnectTask.ConfigureAwait(false);
        }
        finally
        {
            _reconnectTask = null;
        }
    }

    /// <summary>执行重连 — 对齐 TS 端 doReconnect</summary>
    private async Task<bool> DoReconnectAsync(CancellationToken ct)
    {
        var result = await BridgeRemoteCore.ReconnectEnvironmentWithSessionAsync(
            _reconnectState.SessionId,
            _reconnectState.EnvironmentId,
            _coreContext.Parameters,
            _transportContext.ApiClient,
            _transportContext.HttpClient,
            _state,
            _coreContext.PollLoop,
            _reconnectState,
            _fs,
            _logger,
            ct).ConfigureAwait(false);

        if (result)
        {
            _state.EnvironmentRecreations = 0;
        }

        return result;
    }

    public async Task TeardownAsync(CancellationToken ct = default)
    {
        // 对齐 TS 端: teardownStarted 防重入
        if (_state.TeardownStarted)
        {
            _logger?.LogDebug("Bridge v1: Teardown already in progress, skipping duplicate call");
            return;
        }
        _state.TeardownStarted = true;

        if (_state.TornDown) return;
        _state.TornDown = true;

        _bridgeState = BridgeState.Closing;

        // 停止定时器
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;
        _pointerRefreshTimer?.Dispose();
        _pointerRefreshTimer = null;

        // Perpetual 模式: 仅本地清理 — 对齐 TS 端
        // 不发送 result、不调用 stopWork、不归档会话、不关闭传输
        // 后端会自动将工作项租约超时回退为 pending（TTL 300s）
        // 下次 daemon 启动读取指针并 reconnectSession 重新排队工作
        if (_coreContext.Parameters.Perpetual)
        {
            _state.FlushGate.Drop();

            // 刷新指针 mtime — 对齐 TS 端: 防止超过 BRIDGE_POINTER_TTL_MS (4h) 后变陈旧
            try
            {
                var pointerService = new BridgePointerService(_fs, _logger);
                await pointerService.WriteAsync(_coreContext.Parameters.Dir, new BridgePointer
                {
                    SessionId = SessionId,
                    EnvironmentId = EnvironmentId,
                    Source = BridgePointerSource.Repl.ToValue(),
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Bridge v1: Perpetual 模式刷新指针失败");
            }

            _logger?.LogInformation(
                "Bridge v1: Perpetual teardown — 保留 env={EnvId} session={SessionId} 在服务器上",
                EnvironmentId, SessionId);

            _bridgeState = BridgeState.Disconnected;
            _coreContext.Parameters.OnStateChange?.Invoke(BridgeState.Disconnected, null);
            BridgeHandle.SetHandle(null);
            return;
        }

        // 非 Perpetual 模式: 完整拆卸 — 对齐 TS 端 teardown
        _coreContext.Parameters.OnStateChange?.Invoke(BridgeState.Closing, null);

        // 对齐 TS 端: 先发 result 消息再 archive+close
        try
        {
            var transport = _coreContext.PollLoop.CurrentTransport;
            if (transport is not null)
            {
                _ = transport.WriteAsync(BridgeMessaging.MakeResultMessage(SessionId), _disposeCts.Token);
            }
        }
        catch (Exception ex) { /* best-effort */ System.Diagnostics.Trace.WriteLine($"[V1BridgeHandle] Send result message failed: {ex.Message}"); }

        // 对齐 TS 端: stopWork + archiveSession 并行执行
        var stopWorkTask = _coreContext.PollLoop.StopAsync(ct);
        var archiveTask = _coreContext.Parameters.ArchiveSession(SessionId, ct);
        try
        {
            await Task.WhenAll(stopWorkTask, archiveTask).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Bridge v1: stopWork/archiveSession 失败（非致命）");
        }

        // 对齐 TS 端: deregisterEnvironment
        try
        {
            await _transportContext.ApiClient.DeregisterEnvironmentAsync(EnvironmentId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Bridge v1: 注销环境失败（非致命）");
        }

        // 清除崩溃恢复指针 — 对齐 TS 端: clearBridgePointer(dir)
        try
        {
            var pointerService = new BridgePointerService(_fs, _logger);
            await pointerService.ClearAsync(_coreContext.Parameters.Dir, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Bridge v1: 清除崩溃恢复指针失败");
        }

        _bridgeState = BridgeState.Disconnected;
        _coreContext.Parameters.OnStateChange?.Invoke(BridgeState.Disconnected, null);
        BridgeHandle.SetHandle(null);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        var transport = _coreContext.PollLoop.CurrentTransport;
        if (transport is not null)
        {
            await transport.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取当前 SSE 序列号高水位 — 对齐 TS 端 BridgeCoreHandle.getSSESequenceNum()
    /// 合并已关闭传输的检查点和当前活跃传输的实时值
    /// </summary>
    public int GetSSESequenceNum()
    {
        var live = _coreContext.PollLoop.CurrentTransport?.GetLastSequenceNum() ?? 0;
        return Math.Max(_state.LastTransportSequenceNum, live);
    }
}
