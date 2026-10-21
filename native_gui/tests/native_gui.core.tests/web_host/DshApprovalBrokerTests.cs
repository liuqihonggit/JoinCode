namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 权限确认桥测试（TASK058 B1）：引擎确认点 → $events waterfall 帧 → 前端回帖 → 决策返回的完整闭环契约。
/// 帧形状契约（前端 dsh-api-gateway parseRemoteEventFrame 硬校验）：
/// waterfall 帧必须且只能含 type/event/eventId/agentId/request 五键，request 禁含 agent/signal。
/// </summary>
public sealed class DshApprovalBrokerTests {
    /// <summary>被测桥（每测试独立实例，避免挂起请求串味）</summary>
    private readonly DshApprovalBroker _broker = new();

    /// <summary>引擎侧确认请求（与 GuiPermissionConfirmationHandler 转发载荷同形状）</summary>
    private static PermissionConfirmationRequest EngineRequest(string tool = "Bash") =>
        new(tool, "权限确认: [黄灯ask] 执行 shell 命令", RequestId: "req-1", RuleContent: null);

    /// <summary>构造前端回帖 outcome（与 $events/result 请求体同形状）</summary>
    private static DshEventOutcome Outcome(string kind, string? value = null) => new() {
        Kind = kind,
        Value = value is null ? null : JsonSerializer.SerializeToElement(value, DshJsonContext.Default.String),
    };

    /// <summary>attach 一个捕获发送帧的假 $events 连接（信号量通知首帧到达，替代盲等轮询）</summary>
    private (List<JsonElement> Sent, SemaphoreSlim Signal) AttachCapture() {
        var sent = new List<JsonElement>();
        var signal = new SemaphoreSlim(0, int.MaxValue);
        _broker.Attach(frame => {
            lock (sent) { sent.Add(frame); }
            signal.Release();
            return Task.CompletedTask;
        }, "client-1");
        return (sent, signal);
    }

    /// <summary>发起一次确认并在首帧发出后完成回帖，返回决策</summary>
    private async Task<PermissionConfirmationDecision> RequestThenSettleAsync(DshEventOutcome outcome) {
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        await _broker.SettleAsync("client-1", FrameEventId(sent), outcome);
        return await pending;
    }

    /// <summary>等待首帧信号（上限 2 秒，超时即测试失败）</summary>
    private static async Task WaitFrameAsync(SemaphoreSlim signal) {
        (await signal.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue("waterfall 帧未在 2 秒内发出");
    }

    /// <summary>取首帧的 eventId</summary>
    private static string FrameEventId(List<JsonElement> sent) {
        lock (sent) { return sent[0].GetProperty("eventId").GetString() ?? string.Empty; }
    }

    /// <summary>取首帧快照（避免与捕获回调并发读写）</summary>
    private static JsonElement FirstFrame(List<JsonElement> sent) {
        lock (sent) { return sent[0]; }
    }

    /// <summary>取最近一帧的 eventId（同一桥发过多次 waterfall 时用）</summary>
    private static string LastFrameEventId(List<JsonElement> sent) {
        lock (sent) { return sent[^1].GetProperty("eventId").GetString() ?? string.Empty; }
    }

    [Fact]
    public async Task Request_WithoutEventsStream_DeniesImmediately() {
        // $events 未连接（引擎先于前端就绪/前端已断开）→ fail-closed 拒绝，不挂起引擎轮次
        var decision = await _broker.RequestAsync(EngineRequest());
        decision.Should().Be(PermissionConfirmationDecision.Deny);
    }

    [Fact]
    public async Task Request_EmitsWaterfallFrame_WithExactContractShape() {
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        try {
            var frame = FirstFrame(sent);
            frame.GetProperty("type").GetString().Should().Be("waterfall");
            frame.GetProperty("event").GetString().Should().Be("approval/request");
            frame.GetProperty("eventId").GetString().Should().NotBeNullOrEmpty();
            frame.GetProperty("agentId").GetString().Should().NotBeNullOrEmpty();
            var request = frame.GetProperty("request");
            request.GetProperty("toolName").GetString().Should().Be("Bash");
            request.GetProperty("reason").GetString().Should().Contain("权限确认");
            // 前端硬校验：request 不得携带 agent/signal 键（前端自己注入）
            request.EnumerateObject().Select(p => p.Name).Should().NotContain("agent").And.NotContain("signal");
            frame.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["type", "event", "eventId", "agentId", "request"]);
        } finally {
            await _broker.SettleAsync("client-1", FrameEventId(sent), Outcome("result", "rejected"));
            await pending;
        }
    }

    [Fact]
    public async Task Settle_AllowedOnce_ReturnsAllow() =>
        (await RequestThenSettleAsync(Outcome("result", "allowed-once"))).Should().Be(PermissionConfirmationDecision.Allow);

    [Fact]
    public async Task Settle_RejectedValue_ReturnsDeny() =>
        (await RequestThenSettleAsync(Outcome("result", "rejected"))).Should().Be(PermissionConfirmationDecision.Deny);

    [Fact]
    public async Task Settle_NextOutcome_FailsClosedAsDeny() =>
        // 无人处理的 waterfall（kind=next）→ fail-closed 拒绝
        (await RequestThenSettleAsync(Outcome("next"))).Should().Be(PermissionConfirmationDecision.Deny);

    [Fact]
    public async Task Settle_RejectedOutcome_FailsClosedAsDeny() =>
        // 监听器抛错（kind=rejected）→ fail-closed 拒绝
        (await RequestThenSettleAsync(new DshEventOutcome { Kind = "rejected", Error = new DshEventErrorBody { Name = "Error", Message = "boom" } }))
            .Should().Be(PermissionConfirmationDecision.Deny);

    [Fact]
    public async Task Settle_UnknownEventId_ReturnsFalse() {
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        await _broker.SettleAsync("client-1", FrameEventId(sent), Outcome("result", "allowed-once"));
        await pending;
        (await _broker.SettleAsync("client-1", "no-such-event", Outcome("result", "allowed-once"))).Should().BeFalse();
    }

    [Fact]
    public async Task Detach_FailsClosed_DeniesPendingAndFuture() {
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        _broker.Detach();
        // 断开时挂起中的请求立即拒绝（不挂引擎轮次）
        (await pending).Should().Be(PermissionConfirmationDecision.Deny);
        // 之后的新请求也直接拒绝
        _broker.RequestAsync(EngineRequest()).IsCompleted.Should().BeTrue();
        (await _broker.RequestAsync(EngineRequest())).Should().Be(PermissionConfirmationDecision.Deny);
    }

    [Fact]
    public async Task Settle_SecondReplyIsIgnored_PendingAlreadySettled() {
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        var eventId = FrameEventId(sent);
        await _broker.SettleAsync("client-1", eventId, Outcome("result", "allowed-once"));
        (await pending).Should().Be(PermissionConfirmationDecision.Allow);
        // 重复回帖幂等：不抛异常，返回 false
        (await _broker.SettleAsync("client-1", eventId, Outcome("result", "rejected"))).Should().BeFalse();
    }

    // ---- 审批审计对（approval/asked + approval/decided，对齐 DSH dsh-user-approval）----

    /// <summary>捕获审计落库调用（宿主注入 Audit 委托的形状：sessionId/type/payload）</summary>
    private List<(string SessionId, string Type, JsonElement Payload)> CaptureAudit() {
        var entries = new List<(string, string, JsonElement)>();
        _broker.Audit = (sessionId, type, payload) => {
            lock (entries) { entries.Add((sessionId, type, payload)); }
            return Task.CompletedTask;
        };
        return entries;
    }

    /// <summary>取审计条目快照（避免与落库回调并发读写）</summary>
    private static List<(string SessionId, string Type, JsonElement Payload)> AuditSnapshot(
        List<(string SessionId, string Type, JsonElement Payload)> entries) {
        lock (entries) { return [.. entries]; }
    }

    /// <summary>发起一次「开放轮次内的确认」并在回帖后返回决策</summary>
    private async Task<PermissionConfirmationDecision> RoundInTurnAsync(DshEventOutcome outcome) {
        _broker.SetAgent("s1");
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        await _broker.SettleAsync("client-1", FrameEventId(sent), outcome);
        return await pending;
    }

    [Fact]
    public async Task Request_InOpenTurn_AuditsAskedBeforeSettling_ThenDecided() {
        var audit = CaptureAudit();
        _broker.SetAgent("s1");
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);

        // 前端尚未回帖：只应有 approval/asked（审计先于等待）
        var askedOnly = AuditSnapshot(audit);
        askedOnly.Should().HaveCount(1);
        askedOnly[0].SessionId.Should().Be("s1");
        askedOnly[0].Type.Should().Be("approval/asked");
        var asked = askedOnly[0].Payload;
        asked.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        asked.GetProperty("toolName").GetString().Should().Be("Bash");
        asked.GetProperty("reason").GetString().Should().Contain("权限确认");
        asked.GetProperty("callId").GetString().Should().Be("req-1");

        await _broker.SettleAsync("client-1", FrameEventId(sent), Outcome("result", "allowed-once"));
        (await pending).Should().Be(PermissionConfirmationDecision.Allow);

        // 回帖后闭合审计对：同 id、outcome=allowed-once
        var pair = AuditSnapshot(audit);
        pair.Should().HaveCount(2);
        pair[1].Type.Should().Be("approval/decided");
        pair[1].Payload.GetProperty("id").GetString().Should().Be(asked.GetProperty("id").GetString());
        pair[1].Payload.GetProperty("outcome").GetString().Should().Be("allowed-once");
    }

    [Theory]
    [InlineData("result", "rejected", "rejected")]
    [InlineData("next", null, "unavailable")]
    public async Task Request_AuditOutcomeVocabulary_MatchesDsh(string kind, string? value, string expected) {
        var audit = CaptureAudit();
        await RoundInTurnAsync(Outcome(kind, value));

        var decided = AuditSnapshot(audit).Single(entry => entry.Type == "approval/decided");
        decided.Payload.GetProperty("outcome").GetString().Should().Be(expected);
    }

    [Fact]
    public async Task Request_ListenerThrew_AuditsUnavailable() {
        var audit = CaptureAudit();
        await RoundInTurnAsync(new DshEventOutcome { Kind = "rejected", Error = new DshEventErrorBody { Name = "Error", Message = "boom" } });

        AuditSnapshot(audit).Single(entry => entry.Type == "approval/decided").Payload.GetProperty("outcome").GetString()
            .Should().Be("unavailable");
    }

    [Fact]
    public async Task Request_WithoutOpenTurn_SkipsAuditButStillDecides() {
        var audit = CaptureAudit();
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        await _broker.SettleAsync("client-1", FrameEventId(sent), Outcome("result", "allowed-once"));

        (await pending).Should().Be(PermissionConfirmationDecision.Allow);
        AuditSnapshot(audit).Should().BeEmpty("没有开放轮次就不该落审计（DSH 要求审计对被轮次包住）");
    }

    [Fact]
    public async Task CloseTurn_SettlesPendingAsCancelled_AndClosesAuditPair() {
        var audit = CaptureAudit();
        _broker.SetAgent("s1");
        var (sent, signal) = AttachCapture();
        var pending = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);

        // 轮次收尾（用户取消/引擎异常）→ 挂起确认结算为 cancelled，不残留
        await _broker.CloseTurnAsync("s1");
        (await pending).Should().Be(PermissionConfirmationDecision.Deny);
        AuditSnapshot(audit).Single(entry => entry.Type == "approval/decided").Payload.GetProperty("outcome").GetString()
            .Should().Be("cancelled");

        // 目标会话已清空：后续请求不再落审计（等价「无开放轮次」）
        audit.Clear();
        var later = _broker.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        await _broker.SettleAsync("client-1", LastFrameEventId(sent), Outcome("result", "rejected"));
        await later;
        AuditSnapshot(audit).Should().BeEmpty();
    }

    /// <summary>构造 $events/result 请求体（DTO + JsonContext，与前端 connection.rpc.call 载荷同形状）</summary>
    private static JsonElement ResultArgs(string clientId, string eventId, DshEventOutcome outcome) =>
        JsonSerializer.SerializeToElement(
            new DshEventsResultArgs { ClientId = clientId, EventId = eventId, Outcome = outcome },
            DshJsonContext.Default.DshEventsResultArgs);

    [Fact]
    public async Task Dispatcher_EventsResult_RoutesSettleAndRejectsMalformedArgs() {
        var services = new DshHostServices(new IO.FileSystem.InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);
        var sent = new ConcurrentQueue<JsonElement>();
        using var signal = new SemaphoreSlim(0, int.MaxValue);
        services.Approvals.Attach(frame => {
            sent.Enqueue(frame);
            signal.Release();
            return Task.CompletedTask;
        }, "client-1");

        var pending = services.Approvals.RequestAsync(EngineRequest());
        await WaitFrameAsync(signal);
        sent.TryDequeue(out var first);
        var eventId = first.GetProperty("eventId").GetString() ?? string.Empty;

        // 正常回帖：ok=true，请求结算为 Allow
        var ok = await dispatcher.HandleAsync("$events/result", ResultArgs("client-1", eventId, Outcome("result", "allowed-once")));
        ok.Ok.Should().BeTrue(ok.Error?.Message);
        (await pending).Should().Be(PermissionConfirmationDecision.Allow);

        // 畸形载荷：稳定引导错误
        var bad = await dispatcher.HandleAsync("$events/result", JsonDocument.Parse("{}").RootElement.Clone());
        bad.Ok.Should().BeFalse();
        bad.Error?.Code.Should().Be("events/result-rejected");
    }

    [Fact]
    public async Task Dispatcher_PermissionPresets_UsesEngineModeEnumAsSingleSource() {
        var dispatcher = new DshRpcDispatcher(new DshHostServices(new IO.FileSystem.InMemoryFileSystem()));

        var result = await dispatcher.HandleAsync("permissionPresets/catalog", default(JsonElement));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var catalog = result.Value.GetValueOrDefault();
        var values = catalog.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()).ToList();
        // 引擎 PermissionMode 枚举 [EnumValue] 值是唯一数据源（plan/auto/ask/bypass/unattended），硬编码三元组已退役
        values.Should().BeEquivalentTo(["plan", "auto", "ask", "bypass", "unattended"]);
        catalog.GetProperty("defaultPreset").GetString().Should().Be("auto");
    }
}
