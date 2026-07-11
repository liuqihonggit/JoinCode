
namespace Core.Bridge;

/// <summary>
/// Env-less 桥句柄实现 — 对齐 TS 端 ReplBridgeHandle
/// </summary>
internal sealed class EnvLessBridgeHandle : IReplBridgeHandle
{
    private readonly IReplBridgeTransport _transport;
    private readonly BridgeEnvLessParams _params;
    private readonly HttpClient _httpClient;
    private readonly BridgeEnvLessConfig _config;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly BridgeInitState _state;
    private readonly BridgeTokenRefreshScheduler _refresh;
    private volatile BridgeState _bridgeState;

    public string SessionId { get; }
    public string EnvironmentId { get; }
    public string SessionIngressUrl { get; }
    public BridgeState State => _bridgeState;

    public EnvLessBridgeHandle(
        BridgeEnvLessSessionContext sessionContext,
        BridgeEnvLessTransportContext transportContext,
        ILogger? logger = null)
    {
        SessionId = sessionContext.Session.SessionId;
        EnvironmentId = sessionContext.Session.EnvironmentId;
        SessionIngressUrl = sessionContext.Session.SessionIngressUrl;
        _state = sessionContext.State;
        _params = sessionContext.Parameters;
        _transport = transportContext.Transport;
        _httpClient = transportContext.HttpClient;
        _config = transportContext.Config;
        _refresh = transportContext.Refresh;
        _logger = logger;
        _bridgeState = BridgeState.Ready;
    }

    /// <summary>写入消息 — 对齐 TS 端 writeMessages: FlushGate + dedup + titleDerivation + toSDKMessages</summary>
    public void WriteMessages(string[] messages)
    {
        if (_state.TornDown || messages.Length == 0) return;

        // 对齐 TS 端: 标题派生闩锁检查 — 在 flushGate 之前扫描
        if (!_state.UserMessageCallbackDone && _params.OnUserMessage is not null)
        {
            var onUserMessage = _params.OnUserMessage;
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

        // 对齐 TS 端: 双层 UUID 去重 — initialMessageUUIDs + recentPostedUUIDs
        var filtered = FilterMessagesByUUID(messages);
        if (filtered.Count == 0) return;

        // 对齐 TS 端: toSDKMessages + writeBatch + session_id 注入
        if (_params.ToSDKMessages is not null)
        {
            var events = new List<string>(filtered.Count * 2);
            foreach (var msg in filtered)
            {
                var sdkMsgs = _params.ToSDKMessages(msg);
                foreach (var sdkMsg in sdkMsgs)
                {
                    events.Add(BridgeMessaging.InjectSessionId(sdkMsg, SessionId));
                }
            }

            if (events.Count > 0)
            {
                _ = _transport.WriteBatchAsync(events, _disposeCts.Token);
            }
        }
        else
        {
            var events = new string[filtered.Count];
            for (var i = 0; i < filtered.Count; i++)
            {
                events[i] = BridgeMessaging.InjectSessionId(filtered[i], SessionId);
            }
            _ = _transport.WriteBatchAsync(events, _disposeCts.Token);
        }
    }

    /// <summary>
    /// 双层 UUID 去重过滤 — 对齐 TS 端 writeMessages 中的 filter 逻辑
    /// </summary>
    private List<string> FilterMessagesByUUID(string[] messages)
    {
        var result = new List<string>(messages.Length);
        foreach (var msg in messages)
        {
            var uuid = BridgeMessaging.ExtractUuid(msg);
            if (uuid is not null)
            {
                if (_state.InitialMessageUUIDs?.Contains(uuid) == true ||
                    _state.RecentPostedUUIDs.Contains(uuid))
                {
                    continue;
                }
            }

            result.Add(msg);

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
        _ = _transport.WriteBatchAsync(messages, _disposeCts.Token);
    }

    /// <summary>发送控制请求 — 对齐 TS 端 sendControlRequest</summary>
    public void SendControlRequest(string requestJson)
    {
        if (_state.TornDown) return;
        // 对齐 TS 端: 401 恢复期间丢弃控制请求，防止发送过时请求
        if (_state.AuthRecoveryInFlight) return;
        // 对齐 TS 端: can_use_tool 子类型 → reportState('requires_action')
        if (requestJson.Contains("\"can_use_tool\"", StringComparison.Ordinal))
        {
            _ = _transport.ReportStateAsync(BridgeSessionActivity.RequiresAction, _disposeCts.Token);
        }
        _ = _transport.WriteAsync(requestJson, _disposeCts.Token);
    }

    /// <summary>发送控制响应 — 对齐 TS 端 sendControlResponse</summary>
    public void SendControlResponse(string responseJson)
    {
        if (_state.TornDown) return;
        // 对齐 TS 端: 401 恢复期间丢弃控制响应
        if (_state.AuthRecoveryInFlight) return;
        // 对齐 TS 端: 发送响应后 reportState('running')
        _ = _transport.ReportStateAsync(BridgeSessionActivity.Running, _disposeCts.Token);
        _ = _transport.WriteAsync(responseJson, _disposeCts.Token);
    }

    /// <summary>发送取消控制请求 — 对齐 TS 端 sendControlCancelRequest</summary>
    public void SendControlCancelRequest(string requestId)
    {
        if (_state.TornDown) return;
        // 对齐 TS 端: 401 恢复期间丢弃取消请求
        if (_state.AuthRecoveryInFlight) return;
        // 对齐 TS 端: 取消请求后 reportState('running')
        _ = _transport.ReportStateAsync(BridgeSessionActivity.Running, _disposeCts.Token);
        // 使用 StringBuilder 避免 JSON 注入
        var json = new System.Text.StringBuilder(128)
            .Append("{\"type\":\"control_cancel_request\",\"request_id\":\"")
            .Append(EscapeJsonString(requestId.AsSpan()))
            .Append("\",\"session_id\":\"")
            .Append(EscapeJsonString(SessionId.AsSpan()))
            .Append("\"}")
            .ToString();
        _ = _transport.WriteAsync(json, _disposeCts.Token);
    }

    /// <summary>转义 JSON 字符串 — 防止注入</summary>
    private static string EscapeJsonString(ReadOnlySpan<char> value)
    {
        var needsEscape = false;
        foreach (var c in value)
        {
            if (c is '"' or '\\' or '\n' or '\r' or '\t') { needsEscape = true; break; }
        }
        if (!needsEscape) return value.ToString();

        var sb = new System.Text.StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>发送结果消息 — 对齐 TS 端 sendResult</summary>
    public void SendResult()
    {
        if (_state.TornDown) return;
        // 对齐 TS 端: 401 恢复期间丢弃结果消息
        if (_state.AuthRecoveryInFlight) return;
        _ = _transport.ReportStateAsync(BridgeSessionActivity.Idle, _disposeCts.Token);
        _ = _transport.WriteAsync(BridgeMessaging.MakeResultMessage(SessionId), _disposeCts.Token);
    }

    /// <summary>优雅关闭 — 对齐 TS 端 teardown</summary>
    public async Task TeardownAsync(CancellationToken ct = default)
    {
        // 对齐 TS 端: teardownStarted 防重入
        if (_state.TeardownStarted)
        {
            _logger?.LogDebug("Bridge: Teardown already in progress, skipping duplicate call");
            return;
        }
        _state.TeardownStarted = true;

        if (_state.TornDown) return;
        _state.TornDown = true;

        _bridgeState = BridgeState.Closing;
        _params.OnStateChange?.Invoke(BridgeState.Closing, null);

        // 对齐 TS 端: refresh.cancelAll() + flushGate.drop()
        _refresh.CancelAll();
        _state.FlushGate.Drop();

        // 取消 init 阶段的异步操作
        _state.InitCts.Cancel();

        // 发送结果消息 — 对齐 TS 端: transport.reportState('idle') + write(makeResultMessage)
        _ = _transport.ReportStateAsync(BridgeSessionActivity.Idle, _disposeCts.Token);
        _ = _transport.WriteAsync(BridgeMessaging.MakeResultMessage(SessionId), _disposeCts.Token);

        // 归档会话
        var accessToken = _params.GetAccessToken();
        await BridgeSessionApi.ArchiveAsync(
            SessionId,
            _params.BaseUrl,
            accessToken,
            _params.OrgUUID,
            _config.TeardownArchiveTimeoutMs,
            _httpClient,
            ct).ConfigureAwait(false);

        // 关闭传输
        _transport.Close();
        _disposeCts.Cancel();

        _bridgeState = BridgeState.Disconnected;
        _params.OnStateChange?.Invoke(BridgeState.Disconnected, null);

        BridgeHandle.SetHandle(null);
    }

    /// <summary>刷新待发消息</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _transport.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取当前 SSE 序列号高水位 — 对齐 TS 端 BridgeCoreHandle.getSSESequenceNum()
    /// 合并已关闭传输的检查点和当前活跃传输的实时值
    /// </summary>
    public int GetSSESequenceNum()
    {
        var live = _transport.GetLastSequenceNum();
        return Math.Max(_state.LastTransportSequenceNum, live);
    }
}
