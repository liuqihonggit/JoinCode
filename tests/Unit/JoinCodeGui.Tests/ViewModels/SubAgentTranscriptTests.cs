namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// 子代理回放数据契约测试 —
/// tracker 必须为每个 agent 保留完整时间线（不受尾部 3 条活动缓冲影响），
/// MainViewModel.OpenAgentTranscriptCommand 必须携带对应运行记录请求打开回放。
/// </summary>
public class SubAgentTranscriptTests
{
    [Fact]
    public void Tracker_ShouldRecordFullTimeline_InOrder()
    {
        var tracker = new SubAgentRunTracker();
        tracker.Observe(ChatStreamEvent.AgentStarted("a1", "explore", "调研", "executor"));
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, ToolName = "Grep", AgentId = "a1" });
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, ToolName = "Grep", AgentId = "a1" });
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.Content, Content = "找到 3 处匹配", AgentId = "a1" });
        tracker.Observe(ChatStreamEvent.AgentFinished("a1", success: true, executionTimeMs: 5_000, finalOutput: "完成"));

        var transcript = tracker.Runs.Single().Transcript;

        transcript.Should().HaveCount(5, "Started/工具起止/正文/Finished 全程留痕，不裁剪");
        transcript[0].Glyph.Should().Be("▶");
        transcript[0].Text.Should().Contain("explore");
        transcript[2].Glyph.Should().Be("✓");
        transcript[2].Text.Should().Contain("Grep");
        transcript[3].Text.Should().Contain("找到 3 处匹配");
        transcript[4].Glyph.Should().Be("■");
        transcript[4].Text.Should().Contain("完成");
    }

    [Fact]
    public void Transcript_ShouldCarryMonotonicTimestamps()
    {
        var tracker = new SubAgentRunTracker();
        tracker.Observe(ChatStreamEvent.AgentStarted("a2"));
        tracker.Observe(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, ToolName = "Read", AgentId = "a2" });

        var times = tracker.Runs.Single().Transcript.Select(t => t.At).ToList();
        times.Should().BeInAscendingOrder();
    }

    [Fact]
    public void OpenTranscriptCommand_ShouldRaiseRequestWithRun()
    {
        var vm = new MainViewModel(
            new JoinCode.Gui.Hosting.PlaceholderChatSession(),
            new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
            new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.PrepareAgentRunTurnForTest();
        vm.HandleSubAgentActivityForTest(ChatStreamEvent.AgentStarted("a9", "plan", "规划", "executor"));

        var runVm = vm.Messages.Single(m => m.Kind == ChatUiMessageKind.AgentRunGroup).AgentRuns!.Single();
        SubAgentRun? requested = null;
        vm.TranscriptRequested += r => requested = r;

        vm.OpenAgentTranscriptCommand.Execute(runVm);

        requested.Should().NotBeNull();
        requested!.AgentId.Should().Be("a9");
    }
}
