namespace Host.Tests.Bridge;

/// <summary>
/// 后台任务事件泵测试（M8-8）— ForkCompleted/TeammateCompleted 事件 → OnBackgroundTask 四参 upsert、
/// 快照全量补发、Bind 前事件丢弃、Start/Stop 幂等。
/// fake 形态：Moq 模拟两查询面（Raise 触发完成事件 + 快照返回值），fake consumer 记录四参调用序列。
/// </summary>
public sealed class BackgroundTaskEventPumpTests {
    /// <summary>记录 OnBackgroundTask 四参调用序列的 fake consumer（其余回调空实现）。</summary>
    private sealed class RecordingConsumer : IEventConsumer {
        public List<(string Kind, string TaskId, string Description, string State)> Calls { get; } = [];

        public void OnText(string content) { }
        public void OnThinking(string thinking) { }
        public void OnToolStart(string toolName, string? toolCallId, string? arguments) { }
        public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) { }
        public void OnToolProgress(string toolName, string progressType, string? progressMessage) { }
        public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) { }
        public void OnTimingSummary(string summary) { }
        public void OnDone(TokenUsage? usage, string? modelId) { }

        public void OnBackgroundTask(string kind, string taskId, string description, string state) =>
            Calls.Add((kind, taskId, description, state));
    }

    private static ForkCompletedEventArgs ForkCompleted(ForkState state, string taskDescription = "检查README") =>
        new() {
            ForkId = "fork-123",
            State = state,
            TaskDescription = taskDescription,
            Result = "ok",
            Error = state == ForkState.Failed ? "boom" : null,
        };

    private static Mock<IForkSubAgentManager> CreateForkManager(params ForkSubAgent[] activeForks) {
        var mock = new Mock<IForkSubAgentManager>();
        mock.Setup(m => m.GetActiveForksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeForks);
        return mock;
    }

    private static Mock<IInProcessTeammateTaskExecutor> CreateTeammateExecutor(params TeammateStateSnapshot[] snapshots) {
        var mock = new Mock<IInProcessTeammateTaskExecutor>();
        mock.Setup(m => m.GetActiveTeammateSnapshotsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);
        return mock;
    }

    private static TeammateStateSnapshot TeammateSnapshot(string teammateId, string task = "调研渲染链") =>
        new(TeammateId: teammateId, ParentSessionId: "sess-1", Task: task, IsIdle: false, TurnCount: 3, LastResult: null);

    /// <summary>ForkCompleted → ("fork", forkId, taskDescription, state) 四参直推 — completed 终态。</summary>
    [Fact]
    public async Task ForkCompleted_PushesFourPartUpsert() {
        var forkManager = CreateForkManager();
        var consumer = new RecordingConsumer();
        await using var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor: null);
        pump.Bind(consumer);
        await pump.StartAsync();

        forkManager.Raise(m => m.ForkCompleted += null, ForkCompleted(ForkState.Completed));

        var call = Assert.Single(consumer.Calls);
        Assert.Equal(("fork", "fork-123", "检查README", "completed"), call);
    }

    /// <summary>ForkState 终态全映射 — failed/cancelled 透传 [EnumValue] 值，merged 折叠 completed。</summary>
    [Theory]
    [InlineData(ForkState.Failed, "failed")]
    [InlineData(ForkState.Cancelled, "cancelled")]
    [InlineData(ForkState.Merged, "completed")]
    public async Task ForkCompleted_MapsForkStateToProtocolStates(ForkState state, string expected) {
        var forkManager = CreateForkManager();
        var consumer = new RecordingConsumer();
        await using var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor: null);
        pump.Bind(consumer);
        await pump.StartAsync();

        forkManager.Raise(m => m.ForkCompleted += null, ForkCompleted(state));

        Assert.Equal(expected, Assert.Single(consumer.Calls).State);
    }

    /// <summary>TeammateCompleted → ("teammate", teammateId, task, 终态) — IsSuccess 二态映射 completed/failed。</summary>
    [Fact]
    public async Task TeammateCompleted_PushesTeammateUpsert() {
        var teammateExecutor = CreateTeammateExecutor();
        var consumer = new RecordingConsumer();
        await using var pump = new BackgroundTaskEventPump(forkManager: null, teammateExecutor: teammateExecutor.Object);
        pump.Bind(consumer);
        await pump.StartAsync();

        teammateExecutor.Raise(m => m.TeammateCompleted += null,
            new TeammateCompletedEventArgs { TeammateId = "teammate-9", Task = "调研渲染链", IsSuccess = true });
        teammateExecutor.Raise(m => m.TeammateCompleted += null,
            new TeammateCompletedEventArgs { TeammateId = "teammate-8", Task = "修复构建", IsSuccess = false });

        Assert.Equal(
            [("teammate", "teammate-9", "调研渲染链", "completed"), ("teammate", "teammate-8", "修复构建", "failed")],
            consumer.Calls);
    }

    /// <summary>PublishSnapshotAsync — fork 快照按实际 State upsert（running 补发 + 终态幂等重发不倒退）、
    /// teammate 快照全发 running（活跃集合含 idle 等待者）。</summary>
    [Fact]
    public async Task PublishSnapshot_UpsertsForksByState_AndTeammatesAsRunning() {
        var forkManager = CreateForkManager(
            new ForkSubAgent { ForkId = "fork-1", ParentSessionId = "sess-1", State = ForkState.Running, CreatedAt = DateTime.UtcNow },
            new ForkSubAgent { ForkId = "fork-2", ParentSessionId = "sess-1", State = ForkState.Completed, CreatedAt = DateTime.UtcNow, Result = "ok" });
        var teammateExecutor = CreateTeammateExecutor(TeammateSnapshot("teammate-1"), TeammateSnapshot("teammate-2", "写单测"));
        var consumer = new RecordingConsumer();
        await using var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor.Object);
        pump.Bind(consumer);

        await pump.PublishSnapshotAsync();

        Assert.Equal(
            [
                ("fork", "fork-1", "", "running"),
                ("fork", "fork-2", "", "completed"),
                ("teammate", "teammate-1", "调研渲染链", "running"),
                ("teammate", "teammate-2", "写单测", "running"),
            ],
            consumer.Calls);
    }

    /// <summary>Bind 前到达的完成事件丢弃（consumer 未就绪零推送）— 组装顺序保证先 Bind 后 Start。</summary>
    [Fact]
    public async Task ForkCompletedBeforeBind_IsDropped() {
        var forkManager = CreateForkManager();
        var consumer = new RecordingConsumer();
        await using var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor: null);
        await pump.StartAsync();

        forkManager.Raise(m => m.ForkCompleted += null, ForkCompleted(ForkState.Completed));
        pump.Bind(consumer);
        forkManager.Raise(m => m.ForkCompleted += null, ForkCompleted(ForkState.Failed, taskDescription: "late"));

        Assert.Equal([("fork", "fork-123", "late", "failed")], consumer.Calls);
    }

    /// <summary>StopAsync 退订后事件不再推送；DisposeAsync 等价 StopAsync（重复退订幂等）。</summary>
    [Fact]
    public async Task StopAsync_Unsubscribes_AndIsIdempotent() {
        var forkManager = CreateForkManager();
        var consumer = new RecordingConsumer();
        var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor: null);
        pump.Bind(consumer);
        await pump.StartAsync();

        await pump.StopAsync();
        await pump.DisposeAsync();
        forkManager.Raise(m => m.ForkCompleted += null, ForkCompleted(ForkState.Completed));

        Assert.Empty(consumer.Calls);
    }

    /// <summary>StartAsync 幂等 — 重复调用不产生双订阅（单事件单推送）。</summary>
    [Fact]
    public async Task StartAsync_Twice_SubscribesOnce() {
        var forkManager = CreateForkManager();
        var consumer = new RecordingConsumer();
        await using var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor: null);
        pump.Bind(consumer);
        await pump.StartAsync();
        await pump.StartAsync();

        forkManager.Raise(m => m.ForkCompleted += null, ForkCompleted(ForkState.Completed));

        Assert.Single(consumer.Calls);
    }

    /// <summary>PublishSnapshotAsync 在 Bind 前调用零推送（快照出口未就绪静默跳过）。</summary>
    [Fact]
    public async Task PublishSnapshot_WithoutBind_IsNoOp() {
        var forkManager = CreateForkManager(
            new ForkSubAgent { ForkId = "fork-1", ParentSessionId = "sess-1", State = ForkState.Running, CreatedAt = DateTime.UtcNow });
        await using var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor: null);

        await pump.PublishSnapshotAsync();
    }

    /// <summary>查询面抛异常降级不中断快照（另一面仍正常推送 — Actor 释放等场景防御）。</summary>
    [Fact]
    public async Task PublishSnapshot_ForkQueryThrows_TeammatesStillPublished() {
        var forkManager = new Mock<IForkSubAgentManager>();
        forkManager.Setup(m => m.GetActiveForksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("actor disposed"));
        var teammateExecutor = CreateTeammateExecutor(TeammateSnapshot("teammate-1"));
        var consumer = new RecordingConsumer();
        await using var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor.Object);
        pump.Bind(consumer);

        await pump.PublishSnapshotAsync();

        Assert.Equal([("teammate", "teammate-1", "调研渲染链", "running")], consumer.Calls);
    }

    /// <summary>两查询面均 null（引擎未注册）— 泵全程零行为，Bind/PublishSnapshot/Stop 均安全。</summary>
    [Fact]
    public async Task NullManagers_PumpIsInert() {
        var consumer = new RecordingConsumer();
        await using var pump = new BackgroundTaskEventPump(forkManager: null, teammateExecutor: null);
        pump.Bind(consumer);
        await pump.StartAsync();

        await pump.PublishSnapshotAsync();

        Assert.Empty(consumer.Calls);
    }
}
