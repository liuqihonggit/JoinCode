namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// 后台代理管理面板 VM 测试 — pill 点击开合、引擎快照刷新、终止命令。
/// 数据经委托注入（fetcher/stopper），不依赖真实引擎会话。
/// 该面板同时是 fork 跨回合终态的权威数据源（直接读引擎运行列表）。
/// </summary>
public class BackgroundAgentsPanelTests {
    private static BackgroundAgentInfo Info(string id, string state = "running") =>
        new(id, Name: "explore", Description: "调研任务", State: state,
            StartedAt: DateTime.Now.AddSeconds(-30), ToolUseCount: 4, TokenCount: 8200);

    [Fact]
    public async Task Toggle_ShouldOpen_AndFetchSnapshot() {
        var fetched = 0;
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => { fetched++; return Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>([Info("a1")]); },
            stopper: (_, _) => Task.FromResult(true));

        await panel.ToggleAndRefreshAsync();

        panel.IsOpen.Should().BeTrue();
        fetched.Should().Be(1);
        panel.Items.Should().ContainSingle(i => i.AgentId == "a1" && i.IsRunning);
        panel.CountText.Should().Contain("1");
    }

    [Fact]
    public async Task Toggle_Twice_ShouldCloseWithoutFetch() {
        var fetched = 0;
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => { fetched++; return Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>([]); },
            stopper: (_, _) => Task.FromResult(true));

        await panel.ToggleAndRefreshAsync();
        await panel.ToggleAndRefreshAsync();

        panel.IsOpen.Should().BeFalse();
        fetched.Should().Be(1, "关闭时不应再拉取");
    }

    [Fact]
    public void ApplySnapshot_ShouldMapFields_AndRunningFlag() {
        var panel = new BackgroundAgentsPanelViewModel(
            _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>([]),
            (_, _) => Task.FromResult(true));

        panel.ApplySnapshot([Info("r1", "running"), Info("d1", "completed")]);

        panel.Items.Should().HaveCount(2);
        var running = panel.Items.First(i => i.AgentId == "r1");
        running.IsRunning.Should().BeTrue();
        running.ElapsedText.Should().Contain("30");
        running.StatsText.Should().Contain("4").And.Contain("8.2k");
        panel.Items.First(i => i.AgentId == "d1").IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_ShouldCallStopper_AndRefresh() {
        var stopped = new List<string>();
        var agents = new List<BackgroundAgentInfo> { Info("a1"), Info("a2") };
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>(agents.ToList()),
            stopper: (id, _) => { stopped.Add(id); agents.RemoveAll(a => a.AgentId == id); return Task.FromResult(true); });
        await panel.ToggleAndRefreshAsync();

        await panel.StopAsync(panel.Items[0].AgentId);

        stopped.Should().ContainSingle(id => id == "a1");
        panel.Items.Select(i => i.AgentId).Should().NotContain("a1", "终止后立即刷新剔除该行");
    }

    [Fact]
    public async Task Stop_WhenEngineRejects_ShouldKeepRow() {
        var agents = new List<BackgroundAgentInfo> { Info("keep") };
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>(agents.ToList()),
            stopper: (_, _) => Task.FromResult(false));
        await panel.ToggleAndRefreshAsync();

        await panel.StopAsync(panel.Items[0].AgentId);

        panel.Items.Should().ContainSingle("引擎拒绝终止时保留该行等待下次刷新");
    }

    [Fact]
    public async Task PauseAll_ShouldCallPauser_ForRunningItemsOnly() {
        var paused = new List<string>();
        var agents = new List<BackgroundAgentInfo> { Info("a1", "running"), Info("a2", "paused"), Info("a3", "completed") };
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>(agents.ToList()),
            stopper: (_, _) => Task.FromResult(true),
            pauser: (id, _) => { paused.Add(id); return Task.FromResult(true); });
        await panel.ToggleAndRefreshAsync();

        await panel.PauseAllAsync();

        paused.Should().ContainSingle(id => id == "a1", "仅 running 状态可暂停");
    }

    [Fact]
    public async Task ResumeAll_ShouldCallResumer_ForPausedItemsOnly() {
        var resumed = new List<string>();
        var agents = new List<BackgroundAgentInfo> { Info("a1", "running"), Info("a2", "paused"), Info("a3", "paused") };
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>(agents.ToList()),
            stopper: (_, _) => Task.FromResult(true),
            resumer: (id, _) => { resumed.Add(id); return Task.FromResult(true); });
        await panel.ToggleAndRefreshAsync();

        await panel.ResumeAllAsync();

        resumed.Should().BeEquivalentTo(["a2", "a3"], "仅 paused 状态可恢复");
    }

    [Fact]
    public async Task StopAll_ShouldCallStopper_ForRunningItemsOnly() {
        var stopped = new List<string>();
        var agents = new List<BackgroundAgentInfo> { Info("a1", "running"), Info("a2", "paused"), Info("a3", "completed") };
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>(agents.ToList()),
            stopper: (id, _) => { stopped.Add(id); return Task.FromResult(true); });
        await panel.ToggleAndRefreshAsync();

        await panel.StopAllAsync();

        stopped.Should().BeEquivalentTo(["a1", "a2"], "running 和 paused 都应终止，completed 不终止");
    }

    [Fact]
    public async Task StopAll_WhenAllCompleted_ShouldCallNoStopper() {
        var stopped = new List<string>();
        var agents = new List<BackgroundAgentInfo> { Info("a1", "completed"), Info("a2", "completed") };
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>(agents.ToList()),
            stopper: (id, _) => { stopped.Add(id); return Task.FromResult(true); });
        await panel.ToggleAndRefreshAsync();

        await panel.StopAllAsync();

        stopped.Should().BeEmpty("completed 状态不应调用 stopper");
    }

    [Fact]
    public async Task ToggleExpand_ValidId_TogglesIsExpanded() {
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>([Info("a1")]),
            stopper: (_, _) => Task.FromResult(true));
        await panel.ToggleAndRefreshAsync();

        panel.Items[0].IsExpanded.Should().BeFalse();
        panel.ToggleExpand("a1");
        panel.Items[0].IsExpanded.Should().BeTrue();
        panel.ToggleExpand("a1");
        panel.Items[0].IsExpanded.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleExpand_UnknownId_NoChange() {
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>([Info("a1")]),
            stopper: (_, _) => Task.FromResult(true));
        await panel.ToggleAndRefreshAsync();

        panel.ToggleExpand("nonexistent");
        panel.Items[0].IsExpanded.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleExpand_WithTracker_FillsActivitiesOnExpand() {
        var tracker = new SubAgentRunTracker();
        tracker.Observe(ChatStreamEvent.AgentStarted("a1", "explore", "调研任务", "executor"));
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, AgentId = "a1", ToolName = "search" });
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>([Info("a1")]),
            stopper: (_, _) => Task.FromResult(true),
            runTracker: tracker);
        await panel.ToggleAndRefreshAsync();

        panel.ToggleExpand("a1");
        panel.Items[0].IsExpanded.Should().BeTrue();
        panel.Items[0].LastActivityText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ToggleExpand_NoTracker_ActivitiesRemainEmpty() {
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>([Info("a1")]),
            stopper: (_, _) => Task.FromResult(true));
        await panel.ToggleAndRefreshAsync();

        panel.ToggleExpand("a1");
        panel.Items[0].IsExpanded.Should().BeTrue();
        panel.Items[0].LastActivityText.Should().BeNull();
        panel.Items[0].VisibleActivities.Should().BeEmpty();
    }
}