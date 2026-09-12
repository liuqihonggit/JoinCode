namespace Sync.Tests.Agents.Doctor;

/// <summary>
/// PatientStateTransitions 单元测试 — 验证病人进程状态转换规则正确性
/// </summary>
public sealed class PatientStateTransitionsTests
{
    [Fact]
    public void IsTerminal_ShouldReturnTrue_OnlyForTerminalStates()
    {
        PatientStateTransitions.IsTerminal(PatientState.Completed).Should().BeTrue();
        PatientStateTransitions.IsTerminal(PatientState.Failed).Should().BeTrue();
        PatientStateTransitions.IsTerminal(PatientState.Hung).Should().BeTrue();
        PatientStateTransitions.IsTerminal(PatientState.Killed).Should().BeTrue();
        PatientStateTransitions.IsTerminal(PatientState.NotStarted).Should().BeFalse();
        PatientStateTransitions.IsTerminal(PatientState.Running).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowNotStartedToRunning()
    {
        PatientStateTransitions.CanTransitionTo(PatientState.NotStarted, PatientState.Running).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowRunningToCompletedFailedHungKilled()
    {
        PatientStateTransitions.CanTransitionTo(PatientState.Running, PatientState.Completed).Should().BeTrue();
        PatientStateTransitions.CanTransitionTo(PatientState.Running, PatientState.Failed).Should().BeTrue();
        PatientStateTransitions.CanTransitionTo(PatientState.Running, PatientState.Hung).Should().BeTrue();
        PatientStateTransitions.CanTransitionTo(PatientState.Running, PatientState.Killed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyTerminalToAnyNonSelf()
    {
        foreach (var terminal in new[] { PatientState.Completed, PatientState.Failed, PatientState.Hung, PatientState.Killed })
        {
            foreach (var target in Enum.GetValues<PatientState>())
            {
                if (target == terminal) continue;

                PatientStateTransitions.CanTransitionTo(terminal, target).Should().BeFalse(
                    $"终态 {terminal} 不应转到 {target}");
            }
        }
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowSelfLoop()
    {
        foreach (var state in Enum.GetValues<PatientState>())
        {
            PatientStateTransitions.CanTransitionTo(state, state).Should().BeTrue();
        }
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyNotStartedToCompleted()
    {
        PatientStateTransitions.CanTransitionTo(PatientState.NotStarted, PatientState.Completed).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyRunningToNotStarted()
    {
        PatientStateTransitions.CanTransitionTo(PatientState.Running, PatientState.NotStarted).Should().BeFalse();
    }
}
