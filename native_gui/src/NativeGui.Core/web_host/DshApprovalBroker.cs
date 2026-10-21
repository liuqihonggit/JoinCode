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
    /// <summary>一次挂起中的确认（eventId 用于回帖结算，approvalId 用于会话审计对）</summary>
    private sealed record Pending(
        string EventId,
        string? SessionId,
        string ApprovalId,
        TaskCompletionSource<ApprovalSettlement> Completion);

    /// <summary>一次确认的结算：引擎决策 + 会话审计结局词汇（allowed-once/rejected/cancelled/unavailable）</summary>
    private readonly record struct ApprovalSettlement(Hosting.PermissionConfirmationDecision Decision, string Outcome);

    /// <summary>前端未连接/已断开（fail-closed，对齐 DSH「无应答者 → unavailable」）</summary>
    private static readonly ApprovalSettlement DeniedUnavailable =
        new(Hosting.PermissionConfirmationDecision.Deny, "unavailable");

    /// <summary>轮次结束时仍挂起（等价 DSH 的 signal aborted → cancelled）</summary>
    private static readonly ApprovalSettlement DeniedCancelled =
        new(Hosting.PermissionConfirmationDecision.Deny, "cancelled");

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    /// <summary>当前 $events 连接的发送通道（null = 前端未连接/已断开）；发送委托已绑定该流的 streamId</summary>
    private (Func<JsonElement, Task> Send, string ClientId)? _events;

    /// <summary>waterfall 帧的目标会话（轮次执行时由 DshTurnRunner 设置；缺省用 host 常量）</summary>
    private volatile string? _agentId;

    /// <summary>
    /// 会话审计事件落库委托（宿主注入：sessionId/type/data → 会话事件日志）；null = 不落审计。
    /// 对齐 DSH：审批必须在开放轮次内落 <c>approval/asked</c> + <c>approval/decided</c> 成对审计，
    /// 否则历史回放会丢失审批记录（或留下崩溃残尾）。
    /// </summary>
    internal Func<string, string, JsonElement, Task>? Audit { get; set; }

    /// <summary>$events 流打开时挂接发送通道（连接级单例；旧连接被新连接替换）</summary>
    /// <param name="send">帧值发送委托（宿主闭包绑定 streamId，保证路由到该逻辑流）</param>
    /// <param name="clientId">就绪帧下发的客户端标识</param>
    internal void Attach(Func<JsonElement, Task> send, string clientId) {
        lock (_gate) { _events = (send, clientId); }
        GuiDiag.Log($"[DSH-APPROVAL] events stream attached client={clientId}");
    }

    /// <summary>连接状态读写锁（volatile 元组不可用；读写互斥保护 _events 与 _pending 的结算一致性）</summary>
    private readonly object _gate = new();

    /// <summary>
    /// 断流补写的审计写入屏障：<see cref="Detach"/> 的审计是后台任务（释放路径禁阻塞，ADR 0093），
    /// 这里累积它们的完成任务，由 <see cref="CloseTurnAsync"/> 在轮次收尾前排空 ——
    /// 否则 <c>approval/decided</c> 可能排到 <c>turn/end</c> 之后，违反 DSH「审计对被开放轮次包住」。
    /// </summary>
    private Task _detachedAudit = Task.CompletedTask;

    /// <summary>$events 流关闭时摘除通道：挂起中的请求立即拒绝（不挂引擎轮次），后续请求直接拒绝</summary>
    internal void Detach() {
        lock (_gate) { _events = null; }
        var orphans = new List<Pending>();
        foreach (var (eventId, pending) in _pending) {
            if (_pending.TryRemove(eventId, out var removed)) {
                orphans.Add(removed);
            }
        }
        // 释放路径禁止阻塞：审计 decided 交后台补写，并把补写任务挂进屏障供轮次收尾排空（无前端可回放，仅日志卫生）
        foreach (var orphan in orphans) {
            var settle = SettleAsync(orphan, DeniedUnavailable);
            lock (_gate) { _detachedAudit = Task.WhenAll(_detachedAudit, settle); }
        }
        GuiDiag.Log("[DSH-APPROVAL] events stream detached: pending requests denied");
    }

    /// <summary>设置 waterfall 目标会话（DshTurnRunner 轮次开始时调用；前端按 agentId 路由到对应会话 scope）</summary>
    internal void SetAgent(string? sessionId) => _agentId = sessionId;

    /// <summary>
    /// 轮次收尾：清空 waterfall 目标会话，把该会话仍挂起的确认结算为 cancelled，并排空断流补写的审计。
    /// （对齐 DSH：signal aborted → cancelled；同时避免挂起请求在轮次结束后残留）
    /// 必须在 <c>Streams.EndTurn</c> 之前调用，审计对才仍被本轮包住。
    /// </summary>
    /// <param name="sessionId">轮次所属会话</param>
    /// <returns>结算与审计落库完成的任务</returns>
    internal async Task CloseTurnAsync(string sessionId) {
        _agentId = null;
        var orphans = new List<Pending>();
        foreach (var (eventId, pending) in _pending) {
            if (string.Equals(pending.SessionId, sessionId, StringComparison.Ordinal) && _pending.TryRemove(eventId, out var removed)) {
                orphans.Add(removed);
            }
        }
        foreach (var orphan in orphans) {
            await SettleAsync(orphan, DeniedCancelled);
        }
        // 排空断流补写（含并发新到的）：审计必须是发起调用者的责任，返回时保证 decided 已落库
        while (true) {
            var barrier = DetachedAuditBarrier();
            await barrier;
            if (!DetachedAuditReplaced(barrier)) {
                break;
            }
        }
    }

    /// <summary>同步读取断流审计屏障（lock 收敛在同步方法内，规避 async 方法持锁，JCC4001）</summary>
    private Task DetachedAuditBarrier() {
        lock (_gate) { return _detachedAudit; }
    }

    /// <summary>同步判定屏障是否已被新到任务替换（同上：避免 async 方法持锁）</summary>
    /// <param name="barrier">先前读取到的屏障</param>
    /// <returns>是否已被替换</returns>
    private bool DetachedAuditReplaced(Task barrier) {
        lock (_gate) { return !ReferenceEquals(_detachedAudit, barrier); }
    }

    /// <summary>
    /// 引擎确认点入口：构造 waterfall 帧发给前端并等待回帖。
    /// $events 未连接 → 立即拒绝；request 载荷只含 toolName/reason/callId（前端硬校验禁含 agent/signal）。
    /// 开放轮次内先落 <c>approval/asked</c> 审计，结算时补 <c>approval/decided</c>。
    /// </summary>
    /// <param name="request">引擎确认请求</param>
    /// <returns>前端决策（allowed-once=Allow；其余一律 Deny）</returns>
    internal async Task<Hosting.PermissionConfirmationDecision> RequestAsync(Hosting.PermissionConfirmationRequest request) {
        var sessionId = _agentId;
        var approvalId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<ApprovalSettlement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventId = Guid.NewGuid().ToString("N");
        var pending = new Pending(eventId, sessionId, approvalId, completion);

        if (sessionId is not null) {
            await AppendAuditAsync(sessionId, "approval/asked", new DshApprovalAskedData {
                Id = approvalId,
                ToolName = request.ToolName,
                CallId = request.RequestId,
                Reason = request.ConfirmationPrompt,
            });
        } else {
            GuiDiag.Log("[DSH-APPROVAL] no open turn: approval audit pair skipped");
        }

        var send = CurrentSend();
        if (send is null) {
            return await SettleAsync(pending, DeniedUnavailable);
        }

        var frame = new DshWaterfallFrame {
            Event = "approval/request",
            EventId = eventId,
            AgentId = sessionId ?? "host",
            Request = JsonSerializer.SerializeToElement(
                new DshApprovalRequestBody {
                    ToolName = request.ToolName,
                    Reason = request.ConfirmationPrompt,
                    CallId = request.RequestId,
                }, DshJsonContext.Default.DshApprovalRequestBody),
        };
        _pending[eventId] = pending;
        try {
            await send(JsonSerializer.SerializeToElement(frame, DshJsonContext.Default.DshWaterfallFrame));
            GuiDiag.Log($"[DSH-APPROVAL] waterfall sent event=approval/request id={eventId} tool={request.ToolName}");
        } catch (Exception ex) {
            _pending.TryRemove(eventId, out _);
            GuiDiag.Log($"[DSH-APPROVAL] waterfall send failed: {ex.Message}");
            return await SettleAsync(pending, DeniedUnavailable);
        }

        return (await completion.Task).Decision;
    }

    /// <summary>
    /// 结算一次挂起：赢家写 <c>approval/decided</c> 审计（只写一次）并唤醒等待方，返回引擎决策。
    /// 落在轮内的正常路径（前端回帖）由本方法闭合审计对；轮次已结束/断流的补写走同一入口。
    /// </summary>
    /// <param name="pending">挂起中的确认</param>
    /// <param name="settlement">决策 + 审计结局词汇</param>
    /// <returns>引擎决策（重复结算返回首次结果）</returns>
    private async Task<Hosting.PermissionConfirmationDecision> SettleAsync(Pending pending, ApprovalSettlement settlement) {
        if (pending.Completion.TrySetResult(settlement)) {
            if (pending.SessionId is { } sessionId) {
                await AppendAuditAsync(sessionId, "approval/decided", new DshApprovalDecidedData {
                    Id = pending.ApprovalId,
                    Outcome = settlement.Outcome,
                });
            }
            GuiDiag.Log($"[DSH-APPROVAL] decided id={pending.ApprovalId} outcome={settlement.Outcome} -> {settlement.Decision}");
        }
        return (await pending.Completion.Task).Decision;
    }

    /// <summary>落一条审批审计事件（委托未注入时静默跳过并记日志）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="type">事件类型（approval/asked 或 approval/decided）</param>
    /// <param name="payload">审计载荷</param>
    private async Task AppendAuditAsync<T>(string sessionId, string type, T payload) {
        if (Audit is not { } audit) {
            GuiDiag.Log($"[DSH-APPROVAL] audit sink missing: {type} not persisted");
            return;
        }
        try {
            await audit(sessionId, type, JsonSerializer.SerializeToElement(payload, DshJsonContext.Default.GetTypeInfo(typeof(T))));
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-APPROVAL] audit append failed type={type}: {ex.Message}");
        }
    }

    /// <summary>同步读取当前发送通道（lock 收敛在同步方法内，规避 async 方法持锁）</summary>
    private Func<JsonElement, Task>? CurrentSend() {
        lock (_gate) { return _events?.Send; }
    }

    /// <summary>
    /// 前端回帖结算：kind=result 且值为 allowed-once → Allow（审计 allowed-once）；kind=result 的其他值 →
    /// 用户显式拒绝（审计 rejected）；kind=next/rejected/缺失 → 无有效应答者（审计 unavailable，fail-closed）。
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

        return SettleAndReportAsync(pending, MapSettlement(outcome));
    }

    /// <summary>回帖载荷 → 审计结局词汇（唯一放行值是 allowed-once）</summary>
    private static ApprovalSettlement MapSettlement(DshEventOutcome? outcome) {
        if (outcome is not { Kind: "result" } || outcome.Value is not { ValueKind: JsonValueKind.String } value) {
            return DeniedUnavailable;
        }
        return value.GetString() == "allowed-once"
            ? new ApprovalSettlement(Hosting.PermissionConfirmationDecision.Allow, "allowed-once")
            : new ApprovalSettlement(Hosting.PermissionConfirmationDecision.Deny, "rejected");
    }

    /// <summary>结算并忽略决策（RPC 回帖只需幂等确认，决策由引擎侧继续消费）</summary>
    private async Task<bool> SettleAndReportAsync(Pending pending, ApprovalSettlement settlement) {
        await SettleAsync(pending, settlement);
        return true;
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
