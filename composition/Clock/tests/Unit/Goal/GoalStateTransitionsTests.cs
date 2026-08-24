namespace Core.Goal.Tests;

/// <summary>
/// GoalStateTransitions 单元测试 — 验证目标状态转换规则正确性
/// </summary>
public sealed class GoalStateTransitionsTests
{
    [Fact]
    public void IsTerminal_ShouldReturnTrue_OnlyForTerminalStates()
    {
        GoalStateTransitions.IsTerminal(GoalStatus.Achieved).Should().BeTrue();
        GoalStateTransitions.IsTerminal(GoalStatus.Unmet).Should().BeTrue();
        GoalStateTransitions.IsTerminal(GoalStatus.BudgetLimited).Should().BeTrue();
        GoalStateTransitions.IsTerminal(GoalStatus.Pursuing).Should().BeFalse();
        GoalStateTransitions.IsTerminal(GoalStatus.Paused).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowPursuingToPausedAchievedUnmetBudgetLimited()
    {
        GoalStateTransitions.CanTransitionTo(GoalStatus.Pursuing, GoalStatus.Paused).Should().BeTrue();
        GoalStateTransitions.CanTransitionTo(GoalStatus.Pursuing, GoalStatus.Achieved).Should().BeTrue();
        GoalStateTransitions.CanTransitionTo(GoalStatus.Pursuing, GoalStatus.Unmet).Should().BeTrue();
        GoalStateTransitions.CanTransitionTo(GoalStatus.Pursuing, GoalStatus.BudgetLimited).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowPausedToPursuingAndUnmet()
    {
        GoalStateTransitions.CanTransitionTo(GoalStatus.Paused, GoalStatus.Pursuing).Should().BeTrue();
        GoalStateTransitions.CanTransitionTo(GoalStatus.Paused, GoalStatus.Unmet).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyPausedToAchievedOrBudgetLimited()
    {
        GoalStateTransitions.CanTransitionTo(GoalStatus.Paused, GoalStatus.Achieved).Should().BeFalse();
        GoalStateTransitions.CanTransitionTo(GoalStatus.Paused, GoalStatus.BudgetLimited).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowTerminalToPursuingAndUnmet()
    {
        foreach (var terminal in new[] { GoalStatus.Achieved, GoalStatus.Unmet, GoalStatus.BudgetLimited })
        {
            GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.Pursuing).Should().BeTrue(
                $"{terminal} 应能转 Pursuing(Start重新开始)");
            GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.Unmet).Should().BeTrue(
                $"{terminal} 应能转 Unmet(Clear放弃)");
        }
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyTerminalToPausedOrAchievedOrBudgetLimited()
    {
        foreach (var terminal in new[] { GoalStatus.Achieved, GoalStatus.Unmet, GoalStatus.BudgetLimited })
        {
            if (terminal == GoalStatus.Achieved)
            {
                GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.Paused).Should().BeFalse();
                GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.BudgetLimited).Should().BeFalse();
            }

            if (terminal == GoalStatus.BudgetLimited)
            {
                GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.Paused).Should().BeFalse();
                GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.Achieved).Should().BeFalse();
            }

            if (terminal == GoalStatus.Unmet)
            {
                GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.Paused).Should().BeFalse();
                GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.Achieved).Should().BeFalse();
                GoalStateTransitions.CanTransitionTo(terminal, GoalStatus.BudgetLimited).Should().BeFalse();
            }
        }
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowSelfLoop()
    {
        foreach (var state in Enum.GetValues<GoalStatus>())
        {
            GoalStateTransitions.CanTransitionTo(state, state).Should().BeTrue();
        }
    }
}
