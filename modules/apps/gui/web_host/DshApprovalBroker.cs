namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 权限确认桥（TASK058 B1，ADR 0143 M2-d）：把引擎权限确认点接到 DSH 前端 approval 对话框。
/// 链路：引擎 <c>GuiPermissionConfirmationHandler.ConfirmAsync</c> → 宿主注入委托 →
/// <c>$events</c> 流发 waterfall 帧（approval/request）→ 前端 composer 内嵌审批面板 →
/// <c>POST /api/$events/result</c> 回帖 → 本桥结算 → 决策返回引擎。
/// fail-closed 全链路：$events 未连接/已断开/监听器无人处理（next）/监听器抛错（rejected）一律拒绝。
/// 决策词汇：前端 allowed-once → Allow；rejected/其他 → Deny（前端无 always-allow 概念）。
/// </summary>
internal sealed class DshApprovalBroker {
    /// <summary>一次挂起中的确认</summary>
    private sealed record Pending(TaskCompletionSource<Hosting.PermissionConfirmationDecision> Decision);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    /// <summary>当前 $events 连接的发送通道（null = 前端未连接/已断开）；发送委托已绑定该流的 streamId</summary>
    private (Func<JsonElement, Task> Send, string ClientId)? _events;

    /// <summary>waterfall 帧的目标会话（轮次执行时由 DshTurnRunner 设置；缺省用 host 常量）</summary>
    private volatile string? _agentId;

    /// <summary>$events 流打开时挂接发送通道（连接级单例；旧连接被新连接替换）</summary>
    /// <param name="send">帧值发送委托（宿主闭包绑定 streamId，保证路由到该逻辑流）</param>
    /// <param name="clientId">就绪帧下发的客户端标识</param>
    internal void Attach(Func<JsonElement, Task> send, string clientId) {
        lock (_gate) { _events = (send, clientId); }
        App.LogDiag($"[DSH-APPROVAL] events stream attached client={clientId}");
    }

    /// <summary>连接状态读写锁（volatile 元组不可用；读写互斥保护 _events 与 _pending 的结算一致性）</summary>
    private readonly object _gate = new();

    /// <summary>$events 流关闭时摘除通道：挂起中的请求立即拒绝（不挂引擎轮次），后续请求直接拒绝</summary>
    internal void Detach() {
        lock (_gate) { _events = null; }
        foreach (var (eventId, pending) in _pending) {
            if (_pending.TryRemove(eventId, out _)) {
                pending.Decision.TrySetResult(Hosting.PermissionConfirmationDecision.Deny);
            }
        }
        App.LogDiag("[DSH-APPROVAL] events stream detached: pending requests denied");
    }

    /// <summary>设置 waterfall 目标会话（DshTurnRunner 轮次开始时调用；前端按 agentId 路由到对应会话 scope）</summary>
    internal void SetAgent(string? sessionId) => _agentId = sessionId;

    /// <summary>
    /// 引擎确认点入口：构造 waterfall 帧发给前端并等待回帖。
    /// $events 未连接 → 立即拒绝；request 载荷只含 toolName/reason/callId（前端硬校验禁含 agent/signal）。
    /// </summary>
    /// <param name="request">引擎确认请求</param>
    /// <returns>前端决策（allowed-once=Allow；其余一律 Deny）</returns>
    internal async Task<Hosting.PermissionConfirmationDecision> RequestAsync(Hosting.PermissionConfirmationRequest request) {
        var send = CurrentSend();
        if (send is null) {
            return Hosting.PermissionConfirmationDecision.Deny;
        }

        var eventId = Guid.NewGuid().ToString("N");
        var frame = new DshWaterfallFrame {
            Event = "approval/request",
            EventId = eventId,
            AgentId = _agentId ?? "host",
            Request = JsonSerializer.SerializeToElement(
                new DshApprovalRequestBody {
                    ToolName = request.ToolName,
                    Reason = request.ConfirmationPrompt,
                    CallId = request.RequestId,
                }, DshJsonContext.Default.DshApprovalRequestBody),
        };
        var completion = new TaskCompletionSource<Hosting.PermissionConfirmationDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[eventId] = new Pending(completion);
        try {
            await send(JsonSerializer.SerializeToElement(frame, DshJsonContext.Default.DshWaterfallFrame));
            App.LogDiag($"[DSH-APPROVAL] waterfall sent event=approval/request id={eventId} tool={request.ToolName}");
        } catch (Exception ex) {
            _pending.TryRemove(eventId, out _);
            App.LogDiag($"[DSH-APPROVAL] waterfall send failed: {ex.Message}");
            return Hosting.PermissionConfirmationDecision.Deny;
        }

        return await completion.Task;
    }

    /// <summary>同步读取当前发送通道（lock 收敛在同步方法内，规避 async 方法持锁）</summary>
    private Func<JsonElement, Task>? CurrentSend() {
        lock (_gate) { return _events?.Send; }
    }

    /// <summary>
    /// 前端回帖结算：kind=result 且值为 allowed-once → Allow；next/rejected/其他值 → Deny。
    /// clientId 与当前 $events 连接不符（前端重连后的旧代际回帖）或未知 eventId（已结算/从未发起）返回 false（幂等，不抛错）。
    /// </summary>
    /// <param name="clientId">回帖声称的客户端标识（校验与当前连接一致）</param>
    /// <param name="eventId">waterfall 帧的 eventId</param>
    /// <param name="outcome">监听器处理结果</param>
    /// <returns>是否结算了一个挂起中的请求</returns>
    internal Task<bool> SettleAsync(string clientId, string eventId, DshEventOutcome? outcome) {
        lock (_gate) {
            if (_events is not { } channel || !string.Equals(channel.ClientId, clientId, StringComparison.Ordinal)) {
                return Task.FromResult(false);
            }
        }
        if (!_pending.TryRemove(eventId, out var pending)) {
            return Task.FromResult(false);
        }

        var decision = Hosting.PermissionConfirmationDecision.Deny;
        if (outcome is { Kind: "result" } && outcome.Value is { ValueKind: JsonValueKind.String } value
            && value.GetString() == "allowed-once") {
            decision = Hosting.PermissionConfirmationDecision.Allow;
        }
        pending.Decision.TrySetResult(decision);
        App.LogDiag($"[DSH-APPROVAL] settled id={eventId} kind={outcome?.Kind} value={outcome?.Value} -> {decision}");
        return Task.FromResult(true);
    }
}

/// <summary>approval/request 的前端载荷（dsh-client-ui-approval PendingApproval 消费 toolName/callId/reason/displayReason）</summary>
internal sealed class DshApprovalRequestBody {
    /// <summary>工具名</summary>
    public string ToolName { get; set; } = "";
    /// <summary>人类可读原因（引擎确认提示文本）</summary>
    public string Reason { get; set; } = "";
    /// <summary>关联的调用 ID（可选）</summary>
    public string? CallId { get; set; }
}
