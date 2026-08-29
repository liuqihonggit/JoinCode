namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// MainViewModel 子代理事件接线测试 —
/// 引擎 AgentStarted/活动/AgentFinished 事件必须归约为一张内嵌运行组卡片（D2 内嵌组合模型），
/// 且子代理 Content 不得污染主对话正文。
/// </summary>
public class MainViewModelSubAgentTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));

    private static ChatStreamEvent Started(string id, string name = "explore") =>
        ChatStreamEvent.AgentStarted(id, name, "调研任务", "executor");

    [Fact]
    public void HandleSubAgentActivity_ShouldInsertSingleGroupCard()
    {
        var vm = CreateVm();
        vm.PrepareAgentRunTurnForTest();

        vm.HandleSubAgentActivityForTest(Started("a1"));
        vm.HandleSubAgentActivityForTest(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, ToolName = "Grep", AgentId = "a1" });

        var cards = vm.Messages.Where(m => m.Kind == ChatUiMessageKind.AgentRunGroup).ToList();
        cards.Should().ContainSingle("多次事件只创建一张组卡片");
        cards[0].AgentRuns.Should().ContainSingle(r => r.AgentId == "a1");
    }

    [Fact]
    public async Task HandleSubAgentActivity_Finished_ShouldFreezeStatsInCard()
    {
        var vm = CreateVm();
        await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout); // 占位助手消息存在（对齐真实回合）
        vm.PrepareAgentRunTurnForTest();

        vm.HandleSubAgentActivityForTest(Started("a1"));
        vm.HandleSubAgentActivityForTest(ChatStreamEvent.AgentFinished("a1", success: true, executionTimeMs: 61_000, finalOutput: "完成"));

        var card = vm.Messages.Single(m => m.Kind == ChatUiMessageKind.AgentRunGroup);
        var runVm = card.AgentRuns!.Single();
        runVm.IsCompleted.Should().BeTrue();
        runVm.StatsText.Should().Contain("1m 01s");
    }

    [Fact]
    public void HandleSubAgentActivity_ShouldRouteMultipleAgentsIntoOneCard()
    {
        var vm = CreateVm();
        vm.PrepareAgentRunTurnForTest();

        vm.HandleSubAgentActivityForTest(Started("a1", "explore"));
        vm.HandleSubAgentActivityForTest(Started("a2", "plan"));

        var card = vm.Messages.Single(m => m.Kind == ChatUiMessageKind.AgentRunGroup);
        card.AgentRuns.Should().HaveCount(2);
    }
}
