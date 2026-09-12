namespace Bridge.Tests;

/// <summary>
/// BridgeMainLifecycleTransitions 单元测试 — 验证 BridgeMain 生命周期状态转换规则正确性
/// </summary>
public sealed class BridgeMainLifecycleTransitionsTests
{
    [Fact]
    public void CanTransitionTo_ShouldAllowCreatedToRunningShuttingDownDisposed()
    {
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.Created, BridgeMainLifecycleState.Running).Should().BeTrue();
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.Created, BridgeMainLifecycleState.ShuttingDown).Should().BeTrue();
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.Created, BridgeMainLifecycleState.Disposed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowRunningToShuttingDownDisposed()
    {
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.Running, BridgeMainLifecycleState.ShuttingDown).Should().BeTrue();
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.Running, BridgeMainLifecycleState.Disposed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowShuttingDownToDisposed()
    {
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.ShuttingDown, BridgeMainLifecycleState.Disposed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyCreatedToCreatedViaNonSelf()
    {
        // Created→Created is self-loop (allowed), but no non-self transition back to Created
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.Running, BridgeMainLifecycleState.Created).Should().BeFalse();
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.ShuttingDown, BridgeMainLifecycleState.Created).Should().BeFalse();
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.Disposed, BridgeMainLifecycleState.Created).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyShuttingDownToRunning()
    {
        BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.ShuttingDown, BridgeMainLifecycleState.Running).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyDisposedToAnyNonSelf()
    {
        foreach (var target in Enum.GetValues<BridgeMainLifecycleState>())
        {
            if (target == BridgeMainLifecycleState.Disposed) continue;

            BridgeMainLifecycleTransitions.CanTransitionTo(BridgeMainLifecycleState.Disposed, target).Should().BeFalse();
        }
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowSelfLoop()
    {
        foreach (var state in Enum.GetValues<BridgeMainLifecycleState>())
        {
            BridgeMainLifecycleTransitions.CanTransitionTo(state, state).Should().BeTrue();
        }
    }

    [Fact]
    public void IsTerminal_ShouldReturnTrue_OnlyForDisposed()
    {
        BridgeMainLifecycleTransitions.IsTerminal(BridgeMainLifecycleState.Disposed).Should().BeTrue();
        BridgeMainLifecycleTransitions.IsTerminal(BridgeMainLifecycleState.Created).Should().BeFalse();
        BridgeMainLifecycleTransitions.IsTerminal(BridgeMainLifecycleState.Running).Should().BeFalse();
        BridgeMainLifecycleTransitions.IsTerminal(BridgeMainLifecycleState.ShuttingDown).Should().BeFalse();
    }
}
