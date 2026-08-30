namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// SubAgentRunTracker 测试 — 多 subAgent 运行态聚合器。
/// 契约对齐 GUI 运行面板需求：状态机流转、尾部 N 条活动环形缓冲、连续同类工具折叠、
/// 展开上限 LRU 驱逐（移植旧 TUI SubAgentCardManager 语义）。
/// </summary>
public class SubAgentRunTrackerTests
{
    private static ChatStreamEvent AgentStarted(string id, string name = "explore", string desc = "调研") =>
        ChatStreamEvent.AgentStarted(id, name, desc, "executor");

    [Fact]
    public void OnStarted_ShouldCreateRunningEntry()
    {
        var tracker = new SubAgentRunTracker();

        tracker.Observe(AgentStarted("a1"));

        tracker.Runs.Should().ContainSingle(r => r.AgentId == "a1");
        var run = tracker.Runs[0];
        run.State.Should().Be(SubAgentRunState.Running);
        run.Name.Should().Be("explore");
        run.Description.Should().Be("调研");
        run.ToolUseCount.Should().Be(0);
    }

    [Fact]
    public void ToolCallStartAndEnd_ShouldTrackCount_AndLastToolName()
    {
        var tracker = new SubAgentRunTracker();
        tracker.Observe(AgentStarted("a1"));

        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, ToolName = "FileRead", AgentId = "a1", ToolCallId = "c1" });
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, ToolName = "FileRead", AgentId = "a1", ToolCallId = "c1" });

        var run = tracker.Runs.Single();
        run.ToolUseCount.Should().Be(1);
        run.LastActivityText.Should().Contain("FileRead");
    }

    [Fact]
    public void Finished_ShouldFreezeStatistics_AndSetTerminalState()
    {
        var tracker = new SubAgentRunTracker();
        tracker.Observe(AgentStarted("a1"));
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, ToolName = "FileRead", AgentId = "a1" });
        tracker.Observe(ChatStreamEvent.AgentFinished("a1", success: true, executionTimeMs: 132_000, finalOutput: "done"));

        var run = tracker.Runs.Single();
        run.State.Should().Be(SubAgentRunState.Completed);
        run.IsSuccess.Should().BeTrue();
        run.ExecutionTimeMs.Should().Be(132_000);
        run.FinalOutput.Should().Be("done");
        run.ToolUseCount.Should().Be(1);

        // 终态后迟到活动不得复活统计
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, ToolName = "Bash", AgentId = "a1" });
        run.ToolUseCount.Should().Be(1);
    }

    [Fact]
    public void Finished_WhenFailed_ShouldBeFailedState()
    {
        var tracker = new SubAgentRunTracker();
        tracker.Observe(AgentStarted("a1"));
        tracker.Observe(ChatStreamEvent.AgentFinished("a1", success: false, finalOutput: "boom"));

        tracker.Runs.Single().State.Should().Be(SubAgentRunState.Failed);
        tracker.Runs.Single().IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ActivityBuffer_ShouldKeepOnlyTailThree()
    {
        var tracker = new SubAgentRunTracker(maxVisibleActivities: 3);
        tracker.Observe(AgentStarted("a1"));
        for (var i = 1; i <= 5; i++)
            tracker.Observe(new ChatStreamEvent
            {
                Type = ChatStreamEventType.ToolCallStart,
                ToolName = $"T{i}",
                AgentId = "a1",
                ToolCallId = $"c{i}"
            });

        tracker.Runs.Single().VisibleActivities.Should().HaveCount(3);
        tracker.Runs.Single().HiddenActivityCount.Should().Be(2);
        tracker.Runs.Single().VisibleActivities.Last().Should().Contain("T5");
    }

    [Fact]
    public void ConsecutiveSearchRead_ShouldCollapseIntoSummary()
    {
        var tracker = new SubAgentRunTracker();
        tracker.Observe(AgentStarted("a1"));
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, ToolName = "Grep", AgentId = "a1", ToolCallId = "c1" });
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, ToolName = "Grep", AgentId = "a1" });
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, ToolName = "Glob", AgentId = "a1", ToolCallId = "c2" });
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, ToolName = "Glob", AgentId = "a1" });

        // 连续搜索/读取类工具 → 折叠成一条计数摘要（JoinCode getSearchReadSummaryText 模式）
        var last = tracker.Runs.Single().VisibleActivities.Last();
        last.Should().Contain("2").And.Contain("搜索");
    }

    [Fact]
    public void ExpandLimit_ShouldEvictOldestExpanded()
    {
        var tracker = new SubAgentRunTracker(maxExpanded: 3);
        foreach (var id in new[] { "a1", "a2", "a3" })
            tracker.Observe(AgentStarted(id));

        tracker.Expand("a1");
        tracker.Expand("a2");
        tracker.Expand("a3");
        tracker.Expand("a4");

        tracker.IsExpanded("a4").Should().BeTrue();
        tracker.IsExpanded("a1").Should().BeFalse("展开第 4 个时最早展开的被驱逐（LRU）");
        tracker.IsExpanded("a2").Should().BeTrue();
    }

    [Fact]
    public void UnknownAgentEvents_ShouldBeIgnoredGracefully()
    {
        var tracker = new SubAgentRunTracker();

        var act = () => tracker.Observe(new ChatStreamEvent
        {
            Type = ChatStreamEventType.ToolCallEnd,
            ToolName = "Bash",
            AgentId = "ghost"
        });

        act.Should().NotThrow();
        tracker.Runs.Should().BeEmpty();
    }

    [Fact]
    public void ParallelAgents_ShouldMaintainIndependentRuns()
    {
        var tracker = new SubAgentRunTracker();
        tracker.Observe(AgentStarted("a1", "explore", "任务A"));
        tracker.Observe(AgentStarted("a2", "plan", "任务B"));
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, ToolName = "Read", AgentId = "a2" });
        tracker.Observe(ChatStreamEvent.AgentFinished("a1", success: true));

        tracker.RunningCount.Should().Be(1);
        tracker.CompletedCount.Should().Be(1);
        tracker.Runs.Should().HaveCount(2);
    }
}
