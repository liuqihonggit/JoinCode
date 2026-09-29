namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// goal 停止按钮测试 — 验证 StopGoalCommand 直接执行 /goal clear。
/// 再三确认（a→b→c 提示词逐级注入）由引擎层 GoalEngine 自动处理，GUI 不重复。
/// </summary>
public class MainViewModelGoalStopTests {
    private static (MainViewModel vm, JoinCode.Gui.Hosting.PlaceholderChatSession session) Create() {
        var session = new JoinCode.Gui.Hosting.PlaceholderChatSession();
        var vm = new MainViewModel(
            session,
            new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
            new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));
        return (vm, session);
    }

    [Fact]
    public async Task StopGoal_ExecutesGoalClearImmediately() {
        var (vm, session) = Create();
        vm.IsGoalRunning = true;
        vm.IsGoalPanelOpen = true;
        await vm.StopGoalCommand.ExecuteAsync(null);
        session.LastExecutedSlashCommand.Should().Be("/goal clear");
        vm.IsGoalRunning.Should().BeFalse();
        vm.IsGoalPanelOpen.Should().BeFalse();
    }

    [Fact]
    public async Task StopGoal_SetsStatusText() {
        var (vm, _) = Create();
        vm.IsGoalRunning = true;
        await vm.StopGoalCommand.ExecuteAsync(null);
        vm.StatusText.Should().Be("goal 已停止");
    }

    [Fact]
    public void ToggleGoalPanel_TogglesIsGoalPanelOpen() {
        var (vm, _) = Create();
        vm.IsGoalPanelOpen.Should().BeFalse();
        vm.ToggleGoalPanelCommand.Execute(null);
        vm.IsGoalPanelOpen.Should().BeTrue();
        vm.ToggleGoalPanelCommand.Execute(null);
        vm.IsGoalPanelOpen.Should().BeFalse();
    }
}
