using JoinCode.Abstractions.LLM.Chat;

using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// AgentRunVm 测试 — SubAgentRun → 可绑定行 VM 的映射契约，
/// 驱动 AgentRunPanelView 的状态点/统计/活动列表/折叠计数绑定。
/// </summary>
public class AgentRunVmTests
{
    private static SubAgentRun CreateRun() => new()
    {
        AgentId = "a1",
        Name = "explore",
        Description = "调研 GUI 方案",
        Role = "executor"
    };

    [Fact]
    public void Refresh_RunningState_ShouldMapHeaderAndGlyph()
    {
        var vm = new AgentRunVm(CreateRun());
        vm.Refresh();

        vm.StateGlyph.Should().Be("●");
        vm.IsRunning.Should().BeTrue();
        vm.HeaderText.Should().Contain("explore").And.Contain("调研 GUI 方案");
    }

    [Fact]
    public void Refresh_Completed_ShouldFreezeDoneStatsWithDuration()
    {
        var run = CreateRun();
        run.ToolUseCount = 14;
        run.State = SubAgentRunState.Completed;
        run.IsSuccess = true;
        run.ExecutionTimeMs = 132_000;

        var vm = new AgentRunVm(run);
        vm.Refresh();

        vm.StateGlyph.Should().Be("✓");
        vm.IsCompleted.Should().BeTrue();
        vm.StatsText.Should().Contain("14").And.Contain("2m 12s");
        vm.StatsText.Should().StartWith("完成");
    }

    [Fact]
    public void Refresh_Failed_ShouldShowFailureGlyph()
    {
        var run = CreateRun();
        run.State = SubAgentRunState.Failed;

        var vm = new AgentRunVm(run);
        vm.Refresh();

        vm.StateGlyph.Should().Be("✗");
        vm.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Refresh_ShouldSyncActivityLines_AndHiddenCount()
    {
        var run = CreateRun();
        run._visibleActivities.AddRange(["正在调用 Grep…", "✓ Grep", "搜索/读取 2 次…"]);
        run.HiddenActivityCount = 4;

        var vm = new AgentRunVm(run);
        vm.Refresh();

        vm.ActivityLines.Should().HaveCount(3);
        vm.ActivityLines.Last().Should().Contain("搜索/读取");
        vm.HiddenText.Should().Contain("+4");
    }

    [Fact]
    public void Refresh_WhenNoHiddenActivities_HiddenTextShouldBeEmpty()
    {
        var vm = new AgentRunVm(CreateRun());
        vm.Refresh();

        vm.HiddenText.Should().BeEmpty();
    }
}
