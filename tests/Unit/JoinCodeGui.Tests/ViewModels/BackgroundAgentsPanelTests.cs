using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// 后台代理管理面板 VM 测试 — pill 点击开合、引擎快照刷新、终止命令。
/// 数据经委托注入（fetcher/stopper），不依赖真实引擎会话。
/// 该面板同时是 fork 跨回合终态的权威数据源（直接读引擎运行列表）。
/// </summary>
public class BackgroundAgentsPanelTests
{
    private static BackgroundAgentInfo Info(string id, string state = "running") =>
        new(id, Name: "explore", Description: "调研任务", State: state,
            StartedAt: DateTime.Now.AddSeconds(-30), ToolUseCount: 4, TokenCount: 8200);

    [Fact]
    public async Task Toggle_ShouldOpen_AndFetchSnapshot()
    {
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
    public async Task Toggle_Twice_ShouldCloseWithoutFetch()
    {
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
    public void ApplySnapshot_ShouldMapFields_AndRunningFlag()
    {
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
    public async Task Stop_ShouldCallStopper_AndRefresh()
    {
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
    public async Task Stop_WhenEngineRejects_ShouldKeepRow()
    {
        var agents = new List<BackgroundAgentInfo> { Info("keep") };
        var panel = new BackgroundAgentsPanelViewModel(
            fetcher: _ => Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>(agents.ToList()),
            stopper: (_, _) => Task.FromResult(false));
        await panel.ToggleAndRefreshAsync();

        await panel.StopAsync(panel.Items[0].AgentId);

        panel.Items.Should().ContainSingle("引擎拒绝终止时保留该行等待下次刷新");
    }
}

