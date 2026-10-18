namespace JoinCode.Gui.Tests.WebView;

/// <summary>
/// WebChatFlow 测试（会话流端到端 v1）：
/// ① Content 事件按序映射为 chat/delta，空文本与 Thinking 不透传；
/// ② 循环结束/异常均保证恰好一条 chat/done 收尾（web 侧据此恢复发送按钮）；
/// ③ 并发发送拒绝、引擎未就绪拒绝（app/error 诱导，不抛）；
/// ④ 引擎异常映射 app/error + chat/done。
/// </summary>
public sealed class WebChatFlowTests {
    [Fact]
    public async Task SendAsync_ContentOnly_MapsDeltasInOrder_SkipsEmptyAndThinking_ThenDone() {
        var session = new ScriptedSession();
        session.Events.AddRange([
            ChatStreamEvent.Text("你"),
            ChatStreamEvent.Thinking("分析中"),
            ChatStreamEvent.Text(""),
            ChatStreamEvent.Text("好"),
            ChatStreamEvent.Done(),
        ]);
        var flow = new WebChatFlow(session);
        var emitted = new List<BridgeMessage>();

        await flow.SendAsync("你好", emitted.Add);

        emitted.Select(m => m.GetType()).Should().Equal(
            typeof(ChatDeltaMessage), typeof(ChatDeltaMessage), typeof(ChatDoneMessage));
        emitted.OfType<ChatDeltaMessage>().Select(d => d.Text).Should().Equal("你", "好");
        session.LastPrompt.Should().Be("你好");
        flow.IsStreaming.Should().BeFalse("结束后必须复位");
    }

    [Fact]
    public async Task SendAsync_StreamThrows_EmitsAppErrorThenDone() {
        var session = new ScriptedSession();
        session.Events.AddRange([ChatStreamEvent.Text("部分"), ChatStreamEvent.Done()]);
        session.ThrowAt = 1;
        var flow = new WebChatFlow(session);
        var emitted = new List<BridgeMessage>();

        await flow.SendAsync("hi", emitted.Add);

        emitted.Select(m => m.GetType()).Should().Equal(
            typeof(ChatDeltaMessage), typeof(AppErrorMessage), typeof(ChatDoneMessage));
        emitted.OfType<AppErrorMessage>().Single().Reason.Should().Contain("发送失败");
        flow.IsStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WhileStreaming_SecondRejected_FirstUnaffected() {
        var session = new ScriptedSession { GateBeforeFirst = true };
        session.Events.AddRange([ChatStreamEvent.Text("一"), ChatStreamEvent.Done()]);
        var flow = new WebChatFlow(session);
        var emitted = new List<BridgeMessage>();

        var first = flow.SendAsync("p1", emitted.Add);
        flow.IsStreaming.Should().BeTrue("首条流已启动且被门控挂起");

        var rejected = new List<BridgeMessage>();
        await flow.SendAsync("p2", rejected.Add);
        rejected.Should().ContainSingle().Which.Should().BeOfType<AppErrorMessage>(
            "并发发送必须被拒绝且不触碰流");

        session.ReleaseGate();
        await first;

        emitted.OfType<ChatDeltaMessage>().Select(d => d.Text).Should().Equal("一");
        emitted.OfType<ChatDoneMessage>().Should().ContainSingle();
        flow.IsStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_NotReady_RejectsWithAppError_NoDone() {
        var session = new ScriptedSession { IsReady = false };
        session.Events.Add(ChatStreamEvent.Text("不应出现"));
        var flow = new WebChatFlow(session);
        var emitted = new List<BridgeMessage>();

        await flow.SendAsync("hi", emitted.Add);

        var error = emitted.Should().ContainSingle().Subject.Should().BeOfType<AppErrorMessage>().Subject;
        error.Reason.Should().Contain("引擎");
        session.LastPrompt.Should().BeNull("未就绪时不得触碰引擎流");
    }

    [Fact]
    public async Task SendAsync_OceWhileTokenAlive_EmitsTimeoutErrorThenDone() {
        var session = new ScriptedSession { ThrowAt = 0, ThrowException = new OperationCanceledException("请求超时") };
        session.Events.Add(ChatStreamEvent.Text("不应出现"));
        var flow = new WebChatFlow(session);
        var emitted = new List<BridgeMessage>();

        await flow.SendAsync("hi", emitted.Add);

        emitted.Select(m => m.GetType()).Should().Equal(
            new[] { typeof(AppErrorMessage), typeof(ChatDoneMessage) },
            "令牌未取消的 OCE=引擎超时，须回错误而非静默");
        emitted.OfType<AppErrorMessage>().Single().Reason.Should().Contain("超时");
        flow.IsStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_CancelledToken_SilentThenExactlyOneDone() {
        var session = new ScriptedSession();
        session.Events.Add(ChatStreamEvent.Text("不应出现"));
        var flow = new WebChatFlow(session);
        var emitted = new List<BridgeMessage>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await flow.SendAsync("hi", emitted.Add, cts.Token);

        emitted.Should().ContainSingle().Which.Should().BeOfType<ChatDoneMessage>(
            "主动取消（窗口关闭）静默收尾：无 app/error，仅恰好一条 done");
        flow.IsStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ThinkingOnly_NoDelta_ExactlyOneDone() {
        var session = new ScriptedSession();
        session.Events.AddRange([ChatStreamEvent.Thinking("纯思考"), ChatStreamEvent.Done()]);
        var flow = new WebChatFlow(session);
        var emitted = new List<BridgeMessage>();

        await flow.SendAsync("hi", emitted.Add);

        emitted.Should().ContainSingle().Which.Should().BeOfType<ChatDoneMessage>(
            "只有 Thinking 无 Content：不透传增量，恰好一条 done");
        flow.IsStreaming.Should().BeFalse();
    }

    /// <summary>脚本化假会话 — 事件序列可控（门控首事件/定点抛异常），其余成员最小桩</summary>
    private sealed class ScriptedSession : IJccChatSession {
        public List<ChatStreamEvent> Events { get; } = [];
        /// <summary>在产出第 N 个事件前抛出（null=不抛）</summary>
        public int? ThrowAt { get; set; }
        /// <summary>ThrowAt 处抛出的异常实例（null=InvalidOperationException 默认）</summary>
        public Exception? ThrowException { get; set; }
        /// <summary>首个事件前等待门控（并发测试用）</summary>
        public bool GateBeforeFirst { get; set; }
        public string? LastPrompt { get; private set; }
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseGate() => _gate.TrySetResult();

        public bool IsReady { get; set; } = true;
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
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["fake"] = ["fake-model"]
            };

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            string message,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            LastPrompt = message;
            if (GateBeforeFirst) {
                await _gate.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < Events.Count; i++) {
                if (ThrowAt == i) {
                    throw ThrowException ?? new InvalidOperationException("引擎崩溃");
                }
                yield return Events[i];
            }
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
        public Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default) => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
        public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
