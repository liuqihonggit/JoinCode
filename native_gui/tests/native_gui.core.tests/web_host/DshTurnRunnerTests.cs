namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 一轮对话执行器测试（ADR 0143 M2-c2）：session/prompt 受理后立刻落用户消息事件，
/// 后台跑引擎并把回复落成助手消息事件；同一会话只允许一轮；不支持的输入与未知会话给引导错误。
/// </summary>
public sealed class DshTurnRunnerTests {
    private const string SessionsDir = "/test/sessions";
    private const string SessionId = "turn-1";

    [Fact]
    public async Task Start_TextPrompt_EmitsUserMessageThenReply() {
        var harness = await Harness.CreateAsync("回答：你好");
        await using var transcripts = harness.Transcripts;

        var started = await harness.Runner.StartAsync(TextRequest("rpc-1", "你好"), CancellationToken.None);

        started.Should().BeNull();
        await harness.WaitForAsync("turn/end");

        harness.Types().Should().Equal(
            "turn/start", "step/start", "user/message", "assistant/message", "step/end", "turn/end");
        harness.Seqls().Should().Equal(0L, 1L, 2L, 3L, 4L, 5L);

        var user = harness.Events[2].Event.Data;
        user.GetProperty("role").GetString().Should().Be("user");
        user.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("你好");
        user.GetProperty("source").GetProperty("rpcId").GetString().Should().Be("rpc-1");
        harness.Events[2].Event.SurfaceOp.Should().Be("append");

        var assistant = harness.Events[3].Event.Data.GetProperty("message");
        assistant.GetProperty("role").GetString().Should().Be("assistant");
        assistant.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("回答：你好");
        harness.Events[5].Event.Data.GetProperty("reason").GetProperty("kind").GetString().Should().Be("completed");
        harness.Engine.LastPrompt.Should().Be("你好");
        harness.Hub.RunningTurn(SessionId).Should().Be(0, "轮次结束后必须释放占用");
    }

    [Fact]
    public async Task Start_WhileRunning_ReturnsBusy() {
        var harness = await Harness.CreateAsync("慢回答", gate: true);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "第一轮"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("user/message");

        var second = await harness.Runner.StartAsync(TextRequest("rpc-2", "第二轮"), CancellationToken.None);

        var error = second ?? throw new InvalidOperationException("并发发送应当被拒绝");
        error.Code.Should().Be("session/busy");
        error.Message.Should().Contain("上一轮仍在生成");
    }

    [Fact]
    public async Task Start_UnknownSession_ReturnsUnknown() {
        var harness = await Harness.CreateAsync("x", createSession: false);
        await using var transcripts = harness.Transcripts;

        var result = await harness.Runner.StartAsync(TextRequest("rpc-1", "hi"), CancellationToken.None);

        var error = result ?? throw new InvalidOperationException("未知会话应当被拒绝");
        error.Code.Should().Be("session/unknown");
    }

    [Fact]
    public async Task Start_NonTextContent_ReturnsGuidance() {
        var harness = await Harness.CreateAsync("x");
        await using var transcripts = harness.Transcripts;

        var result = await harness.Runner.StartAsync(new DshSessionPromptArgs {
            RequestId = "rpc-1",
            SessionId = SessionId,
            Content = [new DshPromptContent { Type = "image", MediaType = "image/png", Data = "AAA" }],
        }, CancellationToken.None);

        var error = result ?? throw new InvalidOperationException("图片输入应当被拒绝");
        error.Code.Should().Be("session/rejected");
        error.Message.Should().Contain("只支持文本");
    }

    [Fact]
    public async Task Start_WithoutEngine_ReturnsNotReady() {
        var fs = new InMemoryFileSystem();
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        await transcripts.EnsureSessionAsync(SessionId, new SessionInfo { Id = SessionId });

        var result = await new DshTurnRunner(new DshHostServices(fs)).StartAsync(
            new DshSessionPromptArgs { RequestId = "r", SessionId = SessionId, Content = [new() { Text = "hi" }] },
            CancellationToken.None);

        var error = result ?? throw new InvalidOperationException("引擎未就绪应当被拒绝");
        error.Code.Should().Be("session/not-ready");
    }

    [Fact]
    public async Task Start_PlanMode_PublishesPlanModeBeforeTurnStart() {
        var harness = await Harness.CreateAsync("计划", permissionMode: PermissionMode.Plan);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "规划一下"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("turn/end");

        harness.Types().First().Should().Be("plan/mode", "计划模式状态必须先于 turn/start 到达前端");
        var planMode = harness.Events[0].Event.Data;
        planMode.GetProperty("active").GetBoolean().Should().BeTrue();
        harness.Events[0].Event.Seq.Should().BeLessThan(harness.Events[1].Event.Seq);
    }

    [Fact]
    public async Task Start_PlanModeTwice_PublishesPlanModeOnce() {
        var harness = await Harness.CreateAsync("计划", permissionMode: PermissionMode.Plan);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "第一轮"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("turn/end");
        (await harness.Runner.StartAsync(TextRequest("rpc-2", "第二轮"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForCountAsync("turn/end", 2);

        harness.Types().Count(type => type == "plan/mode").Should().Be(1, "计划模式未变化时不得重复写事件");
    }

    [Fact]
    public async Task Start_AutoAfterPlan_PublishesInactivePlanMode() {
        var harness = await Harness.CreateAsync("先计划", permissionMode: PermissionMode.Plan);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "第一轮"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("turn/end");

        harness.Engine.Mode = PermissionMode.Auto;
        (await harness.Runner.StartAsync(TextRequest("rpc-2", "第二轮"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForCountAsync("plan/mode", 2);

        var modes = harness.Events.Where(entry => entry.Event.Type == "plan/mode").ToList();
        modes[0].Event.Data.GetProperty("active").GetBoolean().Should().BeTrue();
        modes[1].Event.Data.GetProperty("active").GetBoolean().Should().BeFalse("离开计划模式必须显式下发 false");
    }

    [Fact]
    public async Task Start_EngineThrows_EndsTurnWithError() {
        var harness = await Harness.CreateAsync("x", throwOnStream: true);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "hi"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("turn/end");

        var reason = harness.Events[^1].Event.Data.GetProperty("reason");
        reason.GetProperty("kind").GetString().Should().Be("error");
        reason.GetProperty("error").GetProperty("message").GetString().Should().Contain("引擎崩溃");
        harness.Types().Should().NotContain("assistant/message");
    }

    [Fact]
    public async Task Cancel_DuringTurn_EndsAbortedByUser() {
        var harness = await Harness.CreateAsync("慢回答", gate: true);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "hi"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("user/message");

        harness.Hub.Cancel(SessionId).Should().BeTrue("有轮次在跑时停止生成必须真正取消");
        await harness.WaitForAsync("turn/end");

        var reason = harness.Events[^1].Event.Data.GetProperty("reason");
        reason.GetProperty("kind").GetString().Should().Be("aborted");
        reason.GetProperty("reason").GetProperty("kind").GetString().Should().Be("user");
        harness.Types().Should().NotContain("assistant/message");
        harness.Hub.RunningTurn(SessionId).Should().Be(0, "取消后必须释放占用");
        harness.Engine.Cancelled.Should().BeTrue("引擎的流必须收到取消令牌");
    }

    [Fact]
    public async Task PublishToOpenSessions_WritesPlanModeToEverySubscribedSession() {
        var hub = new DshSessionStreamHub();
        var first = new ConcurrentQueue<string>();
        var second = new ConcurrentQueue<string>();
        using var leaseA = hub.Subscribe("s1", "stream-a", sessionEvent => {
            first.Enqueue(sessionEvent.Type);
            return Task.CompletedTask;
        });
        using var leaseB = hub.Subscribe("s2", "stream-b", sessionEvent => {
            second.Enqueue(sessionEvent.Type);
            return Task.CompletedTask;
        });

        await hub.PublishToOpenSessionsAsync("plan/mode", new DshPlanModeData { Active = true });

        first.Should().ContainSingle().Which.Should().Be("plan/mode");
        second.Should().ContainSingle().Which.Should().Be("plan/mode");
        hub.LastPlanMode("s1").Should().BeTrue();
        hub.LastPlanMode("never-opened").Should().BeFalse("无记录视为非计划模式");
    }

    [Fact]
    public void Cancel_WithoutRunningTurn_ReturnsFalse() {
        var hub = new DshSessionStreamHub();

        hub.Cancel("idle-session").Should().BeFalse();
    }

    [Fact]
    public async Task Queue_WhileRunning_RunsAfterCurrentTurn() {
        var harness = await Harness.CreateAsync("排队回复", gate: true);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "第一条"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("user/message");

        (await harness.Runner.StartAsync(QueueRequest("rpc-2", "第二条"), CancellationToken.None)).Should().BeNull("mode=queue 在忙时应受理而不是拒绝");
        harness.Types().Count(type => type == "turn/start").Should().Be(1, "排队输入不得抢占当前轮");

        harness.Engine.Release();
        await harness.WaitForCountAsync("turn/end", 2);

        harness.Types().Count(type => type == "turn/start").Should().Be(2);
        harness.Types().Count(type => type == "user/message").Should().Be(2);
        harness.Hub.RunningTurn(SessionId).Should().Be(0, "两条排队轮次都结束后必须释放占用");
    }

    [Fact]
    public async Task SystemNotice_NotMixedIntoAssistantReply() {
        var harness = await Harness.CreateAsync("真正的回复", emitSystemNotice: true);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "你好"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("turn/end");

        harness.Types().Should().NotContain("system/message");
        var assistant = harness.Events.Single(entry => entry.Event.Type == "assistant/message").Event.Data.GetProperty("message");
        assistant.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("真正的回复", "系统旁白不得混入助手回复正文");
    }

    [Fact]
    public async Task ToolCalls_PublishCallAndResultEvents() {
        var harness = await Harness.CreateAsync("已读取", emitToolCalls: true);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "读一下 a.txt"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("turn/end");

        harness.Types().Should().Equal(
            "turn/start", "step/start", "user/message",
            "tool/call", "tool/result", "assistant/message", "step/end", "turn/end");

        var call = harness.Events[3].Event.Data;
        call.GetProperty("callId").GetString().Should().Be("call-1");
        call.GetProperty("name").GetString().Should().Be("Read");
        call.GetProperty("arguments").GetString().Should().Be("{\"path\":\"a.txt\"}");
        call.GetProperty("turn").GetInt32().Should().Be(1);
        call.GetProperty("step").GetInt32().Should().Be(1);

        var result = harness.Events[4].Event.Data.GetProperty("message");
        result.GetProperty("role").GetString().Should().Be("tool");
        result.GetProperty("toolCallId").GetString().Should().Be("call-1");
        result.GetProperty("source").GetProperty("callId").GetString().Should().Be("call-1");
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("ok");
        result.GetProperty("isError").GetBoolean().Should().BeFalse();
        harness.Events[4].Event.SurfaceOp.Should().Be("append");
    }

    [Fact]
    public async Task ToolCallFailed_MarksResultAsError() {
        var harness = await Harness.CreateAsync("失败", emitToolCalls: true, toolFails: true);
        await using var transcripts = harness.Transcripts;

        (await harness.Runner.StartAsync(TextRequest("rpc-1", "读一下"), CancellationToken.None)).Should().BeNull();
        await harness.WaitForAsync("turn/end");

        var result = harness.Events.Single(entry => entry.Event.Type == "tool/result").Event.Data.GetProperty("message");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
    }

    private static DshSessionPromptArgs TextRequest(string requestId, string text) => new() {
        RequestId = requestId,
        SessionId = SessionId,
        Mode = "direct",
        Content = [new DshPromptContent { Type = "text", Text = text }],
    };

    private static DshSessionPromptArgs QueueRequest(string requestId, string text) => new() {
        RequestId = requestId,
        SessionId = SessionId,
        Mode = "queue",
        Content = [new DshPromptContent { Type = "text", Text = text }],
    };

    /// <summary>测试装配：runner + 中枢 + 引擎桩 + 事件收集（用信号量唤醒等待，不做真实延时）</summary>
    private sealed class Harness {
        private readonly SemaphoreSlim _signal = new(0);

        private Harness(DshTurnRunner runner, DshSessionStreamHub hub, StubChatSession engine, State.TranscriptService transcripts) {
            Runner = runner;
            Hub = hub;
            Engine = engine;
            Transcripts = transcripts;
        }

        public DshTurnRunner Runner { get; }
        public DshSessionStreamHub Hub { get; }
        public StubChatSession Engine { get; }
        public State.TranscriptService Transcripts { get; }
        public List<DshSessionRecord> Events { get; } = [];

        public static async Task<Harness> CreateAsync(string reply, bool gate = false, bool createSession = true, bool throwOnStream = false, bool emitToolCalls = false, bool toolFails = false, bool emitSystemNotice = false, PermissionMode permissionMode = PermissionMode.Auto) {
            var fs = new InMemoryFileSystem();
            var services = new DshHostServices(fs);
            var transcripts = new State.TranscriptService(fs, SessionsDir);
            services.Sessions.AttachTranscripts(transcripts);
            if (createSession) {
                await transcripts.EnsureSessionAsync(SessionId, new SessionInfo { Id = SessionId, ProjectPath = "/ws" });
            }
            var engine = new StubChatSession { Reply = reply, Gate = gate, ThrowOnStream = throwOnStream, EmitToolCalls = emitToolCalls, ToolFails = toolFails, EmitSystemNotice = emitSystemNotice, Mode = permissionMode };
            services.AttachEngine(engine);
            var harness = new Harness(new DshTurnRunner(services), services.Streams, engine, transcripts);
            services.Streams.Subscribe(SessionId, "stream-1", sessionEvent => {
                harness.Events.Add(new DshSessionRecord { Event = sessionEvent });
                harness._signal.Release();
                return Task.CompletedTask;
            });
            return harness;
        }

        /// <summary>等某类事件出现（超时即抛，防测试挂死）</summary>
        public async Task WaitForAsync(string type) {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!Events.Any(entry => entry.Event.Type == type)) {
                await _signal.WaitAsync(timeout.Token);
            }
        }

        /// <summary>等某类事件累计出现指定次数（超时即抛，防测试挂死）</summary>
        public async Task WaitForCountAsync(string type, int count) {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Events.Count(entry => entry.Event.Type == type) < count) {
                await _signal.WaitAsync(timeout.Token);
            }
        }

        public IEnumerable<string> Types() => Events.Select(entry => entry.Event.Type);

        public IEnumerable<long> Seqls() => Events.Select(entry => entry.Event.Seq);
    }

    /// <summary>脚本化引擎桩 — 产出固定回复，可门控/抛异常</summary>
    private sealed class StubChatSession : JoinCode.Gui.Hosting.IJccChatSession {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Reply { get; init; } = "";
        public bool Gate { get; init; }
        public bool ThrowOnStream { get; init; }
        public bool EmitToolCalls { get; init; }
        public bool ToolFails { get; init; }
        public bool EmitSystemNotice { get; init; }
        public string? LastPrompt { get; private set; }

        /// <summary>当前权限模式（计划模式事件投影来源）</summary>
        public PermissionMode Mode { get; set; } = PermissionMode.Auto;

        /// <summary>引擎流是否收到了取消（验证停止生成确实传到引擎）</summary>
        public bool Cancelled { get; private set; }

        /// <summary>放行被门控的引擎流（排队测试用）</summary>
        public void Release() => _gate.TrySetResult();

        public bool IsReady => true;
        public ITranscriptService? TranscriptService => null;
#pragma warning disable CS0067
        public event Action? ExitRequested;
#pragma warning restore CS0067
        public Func<string, bool>? SlashConfirmHandler { get; set; }
        public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }
        public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }
        public string CurrentVendor => "fake";
        public string CurrentModelId => "fake-model";
        public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; }
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["fake"] = ["fake-model"] };

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            string message,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            LastPrompt = message;
            if (Gate) {
                try {
                    await _gate.Task.WaitAsync(cancellationToken);
                } catch (OperationCanceledException) {
                    Cancelled = true;
                    throw;
                }
            }
            if (ThrowOnStream) {
                throw new InvalidOperationException("引擎崩溃");
            }
            if (EmitToolCalls) {
                yield return ChatStreamEvent.ToolStart("Read", "call-1", "{\"path\":\"a.txt\"}");
                yield return ChatStreamEvent.ToolEnd("Read", ToolFails ? "文件不存在" : "ok", "call-1", ToolFails);
            }
            if (EmitSystemNotice) {
                yield return ChatStreamEvent.SystemNotice("系统旁白不得出现");
            }
            yield return ChatStreamEvent.Text(Reply);
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default) => Task.FromResult(new RewindResult());
        public Task SetModelAsync(string modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RefreshVendorModelMap() { }
        public void SwitchSession(string sessionId) { }
        public Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public EffortLevel EffortLevel => EffortLevel.Auto;
        public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public float? Temperature => null;
        public int? MaxTokens => null;
        public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() => [];
        public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
        public void UpdateToolBlacklist(HashSet<string> blacklistedTools) { }
        public void UpdateProtectedDrives(HashSet<string> protectedDrives) { }
        public Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default) => Task.FromResult(new ToolResult { IsError = true, Content = [new() { Text = "mock" }] });
        public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default) => Task.FromResult<ToolInfo?>(null);
        public Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default) {
            Mode = mode;
            return Task.CompletedTask;
        }
        public Task<PermissionMode> GetCurrentPermissionModeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Mode);
        public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default) => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
        public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
