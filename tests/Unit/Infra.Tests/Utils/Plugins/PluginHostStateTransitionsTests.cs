namespace Infra.Tests.Utils.Plugins;

/// <summary>
/// PluginHostStateTransitions 单元测试 — 验证插件宿主状态转换规则正确性
/// </summary>
public sealed class PluginHostStateTransitionsTests
{
    [Fact]
    public void CanTransitionTo_ShouldAllowLoadedToUnloadedForceKilled()
    {
        PluginHostStateTransitions.CanTransitionTo(PluginHostState.Loaded, PluginHostState.Unloaded).Should().BeTrue();
        PluginHostStateTransitions.CanTransitionTo(PluginHostState.Loaded, PluginHostState.ForceKilled).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowUnloadedToDisposed()
    {
        PluginHostStateTransitions.CanTransitionTo(PluginHostState.Unloaded, PluginHostState.Disposed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowForceKilledToDisposed()
    {
        PluginHostStateTransitions.CanTransitionTo(PluginHostState.ForceKilled, PluginHostState.Disposed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyLoadedToDisposed()
    {
        PluginHostStateTransitions.CanTransitionTo(PluginHostState.Loaded, PluginHostState.Disposed).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyDisposedToAnyNonSelf()
    {
        foreach (var target in Enum.GetValues<PluginHostState>())
        {
            if (target == PluginHostState.Disposed) continue;

            PluginHostStateTransitions.CanTransitionTo(PluginHostState.Disposed, target).Should().BeFalse();
        }
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowSelfLoop()
    {
        foreach (var state in Enum.GetValues<PluginHostState>())
        {
            PluginHostStateTransitions.CanTransitionTo(state, state).Should().BeTrue();
        }
    }

    [Fact]
    public void IsTerminal_ShouldReturnTrue_OnlyForDisposed()
    {
        PluginHostStateTransitions.IsTerminal(PluginHostState.Disposed).Should().BeTrue();
        PluginHostStateTransitions.IsTerminal(PluginHostState.Loaded).Should().BeFalse();
        PluginHostStateTransitions.IsTerminal(PluginHostState.Unloaded).Should().BeFalse();
        PluginHostStateTransitions.IsTerminal(PluginHostState.ForceKilled).Should().BeFalse();
    }
}
