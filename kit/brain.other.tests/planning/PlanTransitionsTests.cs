namespace Core.Tests.Planning;

/// <summary>
/// PlanStateTransitions + PlanStepTransitions 单元测试 — 验证计划状态转换规则正确性
/// </summary>
public sealed class PlanTransitionsTests
{
    public sealed class PlanStateTransitionsTests
    {
        [Fact]
        public void IsTerminal_ShouldReturnTrue_OnlyForTerminalStates()
        {
            PlanStateTransitions.IsTerminal(PlanStatus.Completed).Should().BeTrue();
            PlanStateTransitions.IsTerminal(PlanStatus.Cancelled).Should().BeTrue();
            PlanStateTransitions.IsTerminal(PlanStatus.Failed).Should().BeTrue();
            PlanStateTransitions.IsTerminal(PlanStatus.Draft).Should().BeFalse();
            PlanStateTransitions.IsTerminal(PlanStatus.AwaitingApproval).Should().BeFalse();
            PlanStateTransitions.IsTerminal(PlanStatus.Executing).Should().BeFalse();
        }

        [Fact]
        public void CanTransitionTo_ShouldAllowDraftToAwaitingApprovalExecutingCancelled()
        {
            PlanStateTransitions.CanTransitionTo(PlanStatus.Draft, PlanStatus.AwaitingApproval).Should().BeTrue();
            PlanStateTransitions.CanTransitionTo(PlanStatus.Draft, PlanStatus.Executing).Should().BeTrue();
            PlanStateTransitions.CanTransitionTo(PlanStatus.Draft, PlanStatus.Cancelled).Should().BeTrue();
        }

        [Fact]
        public void CanTransitionTo_ShouldAllowExecutingToCompletedFailedCancelled()
        {
            PlanStateTransitions.CanTransitionTo(PlanStatus.Executing, PlanStatus.Completed).Should().BeTrue();
            PlanStateTransitions.CanTransitionTo(PlanStatus.Executing, PlanStatus.Failed).Should().BeTrue();
            PlanStateTransitions.CanTransitionTo(PlanStatus.Executing, PlanStatus.Cancelled).Should().BeTrue();
        }

        [Fact]
        public void CanTransitionTo_ShouldDenyTerminalToAnyNonSelf()
        {
            foreach (var terminal in new[] { PlanStatus.Completed, PlanStatus.Cancelled, PlanStatus.Failed })
            {
                foreach (var target in Enum.GetValues<PlanStatus>())
                {
                    if (target == terminal) continue;

                    PlanStateTransitions.CanTransitionTo(terminal, target).Should().BeFalse(
                        $"终态 {terminal} 不应转到 {target}");
                }
            }
        }

        [Fact]
        public void CanTransitionTo_ShouldAllowSelfLoop()
        {
            foreach (var state in Enum.GetValues<PlanStatus>())
            {
                PlanStateTransitions.CanTransitionTo(state, state).Should().BeTrue();
            }
        }

        [Fact]
        public void CanTransitionTo_ShouldDenyCompletedToExecuting()
        {
            PlanStateTransitions.CanTransitionTo(PlanStatus.Completed, PlanStatus.Executing).Should().BeFalse();
        }
    }

    public sealed class PlanStepTransitionsTests
    {
        [Fact]
        public void IsTerminal_ShouldReturnTrue_OnlyForTerminalStates()
        {
            PlanStepTransitions.IsTerminal(PlanStepStatus.Completed).Should().BeTrue();
            PlanStepTransitions.IsTerminal(PlanStepStatus.Failed).Should().BeTrue();
            PlanStepTransitions.IsTerminal(PlanStepStatus.Skipped).Should().BeTrue();
            PlanStepTransitions.IsTerminal(PlanStepStatus.Pending).Should().BeFalse();
            PlanStepTransitions.IsTerminal(PlanStepStatus.Approved).Should().BeFalse();
            PlanStepTransitions.IsTerminal(PlanStepStatus.Rejected).Should().BeFalse();
            PlanStepTransitions.IsTerminal(PlanStepStatus.Executing).Should().BeFalse();
        }

        [Fact]
        public void CanTransitionTo_ShouldAllowPendingToApprovedRejectedSkipped()
        {
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Pending, PlanStepStatus.Approved).Should().BeTrue();
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Pending, PlanStepStatus.Rejected).Should().BeTrue();
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Pending, PlanStepStatus.Skipped).Should().BeTrue();
        }

        [Fact]
        public void CanTransitionTo_ShouldAllowApprovedToExecuting()
        {
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Approved, PlanStepStatus.Executing).Should().BeTrue();
        }

        [Fact]
        public void CanTransitionTo_ShouldAllowRejectedToPendingOrApproved()
        {
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Rejected, PlanStepStatus.Pending).Should().BeTrue();
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Rejected, PlanStepStatus.Approved).Should().BeTrue();
        }

        [Fact]
        public void CanTransitionTo_ShouldAllowExecutingToCompletedFailed()
        {
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Executing, PlanStepStatus.Completed).Should().BeTrue();
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Executing, PlanStepStatus.Failed).Should().BeTrue();
        }

        [Fact]
        public void CanTransitionTo_ShouldDenyTerminalToAnyNonSelf()
        {
            foreach (var terminal in new[] { PlanStepStatus.Completed, PlanStepStatus.Failed, PlanStepStatus.Skipped })
            {
                foreach (var target in Enum.GetValues<PlanStepStatus>())
                {
                    if (target == terminal) continue;

                    PlanStepTransitions.CanTransitionTo(terminal, target).Should().BeFalse(
                        $"终态 {terminal} 不应转到 {target}");
                }
            }
        }

        [Fact]
        public void CanTransitionTo_ShouldAllowSelfLoop()
        {
            foreach (var state in Enum.GetValues<PlanStepStatus>())
            {
                PlanStepTransitions.CanTransitionTo(state, state).Should().BeTrue();
            }
        }

        [Fact]
        public void CanTransitionTo_ShouldDenyCompletedToExecuting()
        {
            PlanStepTransitions.CanTransitionTo(PlanStepStatus.Completed, PlanStepStatus.Executing).Should().BeFalse();
        }
    }
}
