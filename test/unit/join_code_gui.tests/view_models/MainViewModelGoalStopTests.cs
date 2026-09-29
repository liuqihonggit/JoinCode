namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// goal 三再三确认停止测试 — 验证 StopGoalCommand 需要三次确认才执行 /goal clear。
/// Feature3+4：GUI goal 进行中按钮 + 三再三确认停止 + 60秒停滞检测。
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
    public void StopGoal_FirstConfirm_StepIs1_NoSlashCommandExecuted() {
        var (vm, session) = Create();
        vm.IsGoalRunning = true;
        vm.StopGoalCommand.Execute(null);
        vm.StopGoalConfirmationStep.Should().Be(1);
        vm.IsStopConfirmationVisible.Should().BeTrue();
        session.LastExecutedSlashCommand.Should().BeNull();
    }

    [Fact]
    public void StopGoal_SecondConfirm_StepIs2_NoSlashCommandExecuted() {
        var (vm, session) = Create();
        vm.IsGoalRunning = true;
        vm.StopGoalCommand.Execute(null);
        vm.StopGoalCommand.Execute(null);
        vm.StopGoalConfirmationStep.Should().Be(2);
        session.LastExecutedSlashCommand.Should().BeNull();
    }

    [Fact]
    public async Task StopGoal_ThirdConfirm_ExecutesGoalClearAndResets() {
        var (vm, session) = Create();
        vm.IsGoalRunning = true;
        vm.IsGoalPanelOpen = true;
        await vm.StopGoalCommand.ExecuteAsync(null);
        await vm.StopGoalCommand.ExecuteAsync(null);
        await vm.StopGoalCommand.ExecuteAsync(null);
        vm.StopGoalConfirmationStep.Should().Be(0);
        vm.IsGoalRunning.Should().BeFalse();
        vm.IsGoalPanelOpen.Should().BeFalse();
        session.LastExecutedSlashCommand.Should().Be("/goal clear");
    }

    [Fact]
    public void CancelStopGoal_ResetsConfirmationStep() {
        var (vm, _) = Create();
        vm.IsGoalRunning = true;
        vm.StopGoalCommand.Execute(null);
        vm.StopGoalCommand.Execute(null);
        vm.CancelStopGoalCommand.Execute(null);
        vm.StopGoalConfirmationStep.Should().Be(0);
        vm.IsStopConfirmationVisible.Should().BeFalse();
    }

    [Fact]
    public void ContinueGoalWait_ClosesPanelAndResetsStep() {
        var (vm, _) = Create();
        vm.IsGoalRunning = true;
        vm.IsGoalPanelOpen = true;
        vm.StopGoalCommand.Execute(null);
        vm.ContinueGoalWaitCommand.Execute(null);
        vm.IsGoalPanelOpen.Should().BeFalse();
        vm.StopGoalConfirmationStep.Should().Be(0);
    }

    [Fact]
    public void StopGoalConfirmationPrompt_Step1ContainsFirstConfirm() {
        var (vm, _) = Create();
        vm.StopGoalConfirmationStep = 1;
        vm.StopGoalConfirmationPrompt.Should().Contain("第 1/3 次确认");
    }

    [Fact]
    public void StopGoalConfirmationPrompt_Step3ContainsFinalConfirm() {
        var (vm, _) = Create();
        vm.StopGoalConfirmationStep = 3;
        vm.StopGoalConfirmationPrompt.Should().Contain("第 3/3 次确认");
    }
}
