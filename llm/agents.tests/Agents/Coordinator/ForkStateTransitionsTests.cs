namespace Sync.Tests.Agents.Coordinator;

/// <summary>
/// ForkStateTransitions 单元测试 — 验证 Fork 状态转换规则集中定义的正确性
/// </summary>
public sealed class ForkStateTransitionsTests
{
    [Fact]
    public void IsTerminal_ShouldReturnTrue_OnlyForTerminalStates()
    {
        ForkStateTransitions.IsTerminal(ForkState.Merged).Should().BeTrue();
        ForkStateTransitions.IsTerminal(ForkState.Cancelled).Should().BeTrue();
        ForkStateTransitions.IsTerminal(ForkState.Failed).Should().BeTrue();
        ForkStateTransitions.IsTerminal(ForkState.Running).Should().BeFalse();
        ForkStateTransitions.IsTerminal(ForkState.Completed).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowRunningToCompletedFailedCancelled()
    {
        ForkStateTransitions.CanTransitionTo(ForkState.Running, ForkState.Completed).Should().BeTrue();
        ForkStateTransitions.CanTransitionTo(ForkState.Running, ForkState.Failed).Should().BeTrue();
        ForkStateTransitions.CanTransitionTo(ForkState.Running, ForkState.Cancelled).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowCompletedToMerged()
    {
        ForkStateTransitions.CanTransitionTo(ForkState.Completed, ForkState.Merged).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyCompletedToRunningOrFailedOrCancelled()
    {
        ForkStateTransitions.CanTransitionTo(ForkState.Completed, ForkState.Running).Should().BeFalse();
        ForkStateTransitions.CanTransitionTo(ForkState.Completed, ForkState.Failed).Should().BeFalse();
        ForkStateTransitions.CanTransitionTo(ForkState.Completed, ForkState.Cancelled).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyAnyTransitionFromTerminalStates()
    {
        foreach (var terminal in new[] { ForkState.Merged, ForkState.Cancelled, ForkState.Failed })
        {
            foreach (var target in Enum.GetValues<ForkState>())
            {
                if (target == terminal) continue;

                ForkStateTransitions.CanTransitionTo(terminal, target).Should().BeFalse(
                    $"终态 {terminal} 不应转到 {target}");
            }
        }
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowSelfLoop()
    {
        foreach (var state in Enum.GetValues<ForkState>())
        {
            ForkStateTransitions.CanTransitionTo(state, state).Should().BeTrue(
                $"自环 {state} → {state} 应合法");
        }
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyRunningToMerged()
    {
        ForkStateTransitions.CanTransitionTo(ForkState.Running, ForkState.Merged).Should().BeFalse();
    }
}
